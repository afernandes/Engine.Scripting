using Engine.Scripting.Hosting;
using Engine.Scripting.Instances;
using Engine.Scripting.Orchestration.Sources;
using Engine.Scripting.StatePreservation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Orchestration;

/// <summary>
/// The composition root of the hot-reload mechanism: watches a script (or precompiled-image)
/// source, debounces change bursts, and runs the full reload pipeline — compile/load, snapshot,
/// cooperative unload, restore, lifecycle hooks — off the caller's thread, surfacing progress
/// through events.
/// </summary>
/// <remarks>
/// <para>
/// Reload runs are serialized by an async gate; change notifications arriving during a run are
/// accumulated and coalesced into at most one follow-up run. Event handlers are isolated: an
/// exception thrown by a subscriber is logged and never affects the pipeline.
/// </para>
/// <para>
/// A failed reload (compilation error, faulted pipeline) leaves the previous generation running
/// — the host never goes down because a script edit was bad. Cancellation (host shutdown)
/// produces a <see cref="ReloadOutcome.Cancelled"/> result and raises no events.
/// </para>
/// </remarks>
public sealed class HotReloadOrchestrator : IAsyncDisposable, IDisposable
{
    private readonly HotReloadOptions _options;
    private readonly ILogger<HotReloadOrchestrator> _logger;
    private readonly ScriptInstanceRegistry _registry;
    private readonly ReloadableScriptContext _context;
    private readonly ReloadPipeline _pipeline;
    private readonly PendingChangeSet _pendingChanges = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly Abstractions.IScriptSource? _source;
    private readonly bool _ownsSource;
    private readonly Abstractions.IScriptAssemblyImageSource? _imageSource;

    private CancellationTokenSource? _lifetimeCts;
    private ChangeDebouncer? _debouncer;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates the orchestrator, validating that exactly one acquisition mode is configured.</summary>
    /// <param name="options">Orchestration options (see <see cref="HotReloadOptions"/> for the modes).</param>
    /// <param name="loggerFactory">Optional logger factory; defaults to no-op logging.</param>
    /// <exception cref="ArgumentException">The options configure zero or both acquisition modes.</exception>
    public HotReloadOrchestrator(HotReloadOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateModes(options);

        _options = options;
        loggerFactory ??= NullLoggerFactory.Instance;
        _logger = loggerFactory.CreateLogger<HotReloadOrchestrator>();

        _registry = new ScriptInstanceRegistry(loggerFactory.CreateLogger<ScriptInstanceRegistry>());
        var statePreservation = new StatePreservationService(loggerFactory.CreateLogger<StatePreservationService>());
        _context = new ReloadableScriptContext(options.Hosting, loggerFactory.CreateLogger<ReloadableScriptContext>());

        if (options.ImageSource is not null)
        {
            _imageSource = options.ImageSource;
        }
        else if (options.Source is not null)
        {
            _source = options.Source;
        }
        else
        {
            _source = new FileSystemScriptSource(
                options.ScriptsPath!,
                options.SearchPattern,
                options.IncludeSubdirectories,
                loggerFactory.CreateLogger<FileSystemScriptSource>());
            _ownsSource = true;
        }

        _pipeline = new ReloadPipeline(options, _context, _registry, statePreservation, _source, _imageSource, _logger);
    }

    /// <summary>Raised when a reload run begins (initial, debounced source change, or manual).</summary>
    public event EventHandler<ReloadStartedEventArgs>? ReloadStarted;

    /// <summary>Raised when a reload run completes and the new generation is active.</summary>
    public event EventHandler<ReloadSucceededEventArgs>? ReloadSucceeded;

    /// <summary>Raised when a reload run does not apply (compilation failure or pipeline fault).</summary>
    public event EventHandler<ReloadFailedEventArgs>? ReloadFailed;

    /// <summary>
    /// Raised when the previous generation failed to collect within the configured timeout —
    /// the memory-leak alarm of the cooperative unload model.
    /// </summary>
    public event EventHandler<AssemblyUnloadTimedOutEventArgs>? AssemblyUnloadTimedOut;

    /// <summary>The registry mapping stable script identities to current instances.</summary>
    public ScriptInstanceRegistry Registry => _registry;

    /// <summary>Number of the currently active generation (0 when none).</summary>
    public int CurrentGenerationNumber => _context.CurrentGeneration?.Number ?? 0;

    /// <summary>
    /// The live generation, or <see langword="null"/> when none is loaded. Hosts use it to scan
    /// the generation assembly right after a reload (system/component discovery). The usual
    /// warning applies: resolve, use, let go — never store the generation, its assembly or
    /// anything reached through them across a reload.
    /// </summary>
    public Hosting.ScriptGeneration? CurrentGeneration => _context.CurrentGeneration;

    /// <summary>
    /// One <see cref="WeakReference"/> probe per retired generation — the observable proof that
    /// past generations were actually collected.
    /// </summary>
    public IReadOnlyList<WeakReference> RetiredGenerationProbes => _context.RetiredGenerationProbes;

    /// <summary>
    /// Performs the initial load and, when <see cref="HotReloadOptions.EnableSourceWatching"/> is
    /// on, starts watching the source with debounced automatic reloads.
    /// </summary>
    /// <param name="cancellationToken">
    /// Governs the whole orchestrator lifetime: cancelling it aborts pending reloads.
    /// </param>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("The orchestrator is already started.");
        }

        _started = true;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var lifetimeToken = _lifetimeCts.Token;

        await RunReloadAsync(ReloadTrigger.Initial, lifetimeToken).ConfigureAwait(false);

        if (_options.EnableSourceWatching)
        {
            _debouncer = new ChangeDebouncer(_options.DebounceInterval, OnQuietPeriodAsync, lifetimeToken, _logger);

            if (_source is not null)
            {
                _source.Changed += OnSourceChanged;
                await _source.StartWatchingAsync(lifetimeToken).ConfigureAwait(false);
            }

            if (_imageSource is not null)
            {
                _imageSource.Changed += OnImageSourceChanged;
                await _imageSource.StartWatchingAsync(lifetimeToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Triggers a full reload immediately (rescanning the whole source), independent of the
    /// watcher. Runs are serialized with watcher-triggered reloads.
    /// </summary>
    /// <param name="cancellationToken">Aborts this reload; linked to the orchestrator lifetime.</param>
    /// <returns>The result of the run.</returns>
    public async Task<ReloadResult> ReloadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_started)
        {
            throw new InvalidOperationException("Call StartAsync before ReloadAsync.");
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts!.Token, cancellationToken);
        var result = await RunReloadAsync(ReloadTrigger.Manual, linked.Token).ConfigureAwait(false);
        return result!;
    }

    /// <summary>
    /// Stops watching, waits for an in-flight reload to finish, then cancels the lifetime.
    /// </summary>
    /// <param name="cancellationToken">Bounds the graceful wait.</param>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_started)
        {
            return;
        }

        _debouncer?.Dispose();
        _debouncer = null;

        if (_source is not null)
        {
            _source.Changed -= OnSourceChanged;
            await _source.StopWatchingAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_imageSource is not null)
        {
            _imageSource.Changed -= OnImageSourceChanged;
            await _imageSource.StopWatchingAsync(cancellationToken).ConfigureAwait(false);
        }

        // Barrier: let an in-flight reload finish cleanly before cancelling the lifetime.
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _reloadGate.Release();

        if (_lifetimeCts is not null)
        {
            await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronous bridge over <see cref="DisposeAsync"/> for containers that dispose
    /// synchronously. Safe here: every await in the dispose path uses
    /// <c>ConfigureAwait(false)</c>, so there is no synchronization context to deadlock on.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    /// <summary>Stops the orchestrator and unloads the current generation.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Log.PipelineFaulted(_logger, exception);
        }

        await _context.DisposeAsync().ConfigureAwait(false);

        if (_ownsSource && _source is not null)
        {
            await _source.DisposeAsync().ConfigureAwait(false);
        }

        _lifetimeCts?.Dispose();
        _reloadGate.Dispose();
    }

    private static void ValidateModes(HotReloadOptions options)
    {
        var imageMode = options.ImageSource is not null;
        var hasSource = options.Source is not null;
        var hasPath = options.ScriptsPath is not null;

        if (imageMode)
        {
            if (hasSource || hasPath || options.Compiler is not null)
            {
                throw new ArgumentException(
                    "Configure either ImageSource (precompiled mode) or Source/ScriptsPath + Compiler (source mode), not both.",
                    nameof(options));
            }

            return;
        }

        if (hasSource == hasPath) // both set, or neither
        {
            throw new ArgumentException(
                "Configure exactly one script origin: Source, ScriptsPath, or ImageSource.",
                nameof(options));
        }

        if (options.Compiler is null)
        {
            throw new ArgumentException(
                "Source mode requires a Compiler (e.g. new IncrementalScriptCompiler(new ScriptCompilerOptions()) from Engine.Scripting.Compilation).",
                nameof(options));
        }
    }

    private void OnSourceChanged(object? sender, Abstractions.ScriptSourceChangedEventArgs e)
    {
        _pendingChanges.Merge(e.ChangedDocumentIds, e.RequiresFullRescan);
        _debouncer?.Signal();
    }

    private void OnImageSourceChanged(object? sender, EventArgs e)
    {
        _pendingChanges.RequestFullRescan();
        _debouncer?.Signal();
    }

    private Task OnQuietPeriodAsync(CancellationToken cancellationToken)
        => RunReloadAsync(ReloadTrigger.SourceChange, cancellationToken);

    private async Task<ReloadResult?> RunReloadAsync(ReloadTrigger trigger, CancellationToken cancellationToken)
    {
        try
        {
            await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return CancelledResult();
        }

        try
        {
            var (changedIds, fullRescan) = _pendingChanges.Drain();
            if (trigger == ReloadTrigger.SourceChange && changedIds.Length == 0 && !fullRescan)
            {
                // A previous run already consumed this batch.
                return null;
            }

            Log.ReloadStarting(_logger, trigger, changedIds.Length);
            RaiseReloadStarted(trigger, changedIds);

            var execution = await _pipeline.ExecuteAsync(trigger, changedIds, fullRescan, cancellationToken).ConfigureAwait(false);

            if (execution.Unload?.Outcome == UnloadOutcome.TimedOut)
            {
                RaiseUnloadTimedOut(execution.RetiredGenerationNumber, execution.Unload.Elapsed);
            }

            var result = execution.Result;
            if (result.Outcome == ReloadOutcome.Succeeded)
            {
                Log.ReloadSucceeded(_logger, result.Generation, (long)result.Duration.TotalMilliseconds, result.UnloadTimedOut);
                RaiseReloadSucceeded(result);
            }
            else
            {
                var errorCount = result.Diagnostics.Count(d => d.Severity == Abstractions.ScriptDiagnosticSeverity.Error);
                Log.ReloadNotApplied(_logger, result.Outcome, errorCount);
                RaiseReloadFailed(result, execution.Exception);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return CancelledResult();
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private ReloadResult CancelledResult()
    {
        var now = DateTimeOffset.UtcNow;
        return new ReloadResult(ReloadOutcome.Cancelled, CurrentGenerationNumber, [], false, TimeSpan.Zero, now, now);
    }

    private void RaiseReloadStarted(ReloadTrigger trigger, IReadOnlyList<string> changedDocumentIds)
    {
        var handler = ReloadStarted;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new ReloadStartedEventArgs(trigger, changedDocumentIds, DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            Log.EventHandlerFailed(_logger, nameof(ReloadStarted), exception);
        }
    }

    private void RaiseReloadSucceeded(ReloadResult result)
    {
        var handler = ReloadSucceeded;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new ReloadSucceededEventArgs(result));
        }
        catch (Exception exception)
        {
            Log.EventHandlerFailed(_logger, nameof(ReloadSucceeded), exception);
        }
    }

    private void RaiseReloadFailed(ReloadResult result, Exception? exception)
    {
        var handler = ReloadFailed;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new ReloadFailedEventArgs(result.Outcome, result.Diagnostics, exception, DateTimeOffset.UtcNow));
        }
        catch (Exception handlerException)
        {
            Log.EventHandlerFailed(_logger, nameof(ReloadFailed), handlerException);
        }
    }

    private void RaiseUnloadTimedOut(int generationNumber, TimeSpan elapsed)
    {
        var handler = AssemblyUnloadTimedOut;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(this, new AssemblyUnloadTimedOutEventArgs(
                generationNumber, _options.Hosting.UnloadTimeout, elapsed, DateTimeOffset.UtcNow));
        }
        catch (Exception exception)
        {
            Log.EventHandlerFailed(_logger, nameof(AssemblyUnloadTimedOut), exception);
        }
    }
}
