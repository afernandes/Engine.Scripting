using System.Diagnostics;
using Engine.Scripting.Abstractions;
using Engine.Scripting.Hosting;
using Engine.Scripting.Instances;
using Engine.Scripting.StatePreservation;
using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Orchestration;

/// <summary>
/// The reload pipeline: acquire the new image (compile sources or load a precompiled image),
/// snapshot state, run before-hooks, detach, cooperatively unload the old generation, load the
/// new one, re-create instances, restore state, run after-hooks.
/// </summary>
/// <remarks>
/// <para><b>Reference discipline (load-bearing).</b> Locals of an <c>async</c> method are hoisted
/// into a heap-allocated state machine and stay GC roots until overwritten — so this class never
/// keeps a local typed as an old-generation instance, <c>Assembly</c> or
/// <see cref="ScriptGeneration"/> alive across the unload-verification await. Everything that
/// touches old instances is confined to <see cref="CaptureAndTeardownAsync"/> and its helpers,
/// which null out their hoisted references before returning. The snapshot dictionary that
/// remains is structurally unable to pin the old context (see <see cref="AlcSafetyInspector"/>).
/// </para>
/// <para>
/// Failure semantics: a compilation failure or an exception during phase A leaves the current
/// generation untouched. A timeout during unload verification is reported but never blocks the
/// new generation. Exceptions in script hooks are logged per instance and never abort the run.
/// </para>
/// </remarks>
internal sealed class ReloadPipeline
{
    private readonly HotReloadOptions _options;
    private readonly ReloadableScriptContext _context;
    private readonly ScriptInstanceRegistry _registry;
    private readonly StatePreservationService _statePreservation;
    private readonly IScriptCompiler? _compiler;
    private readonly IScriptSource? _source;
    private readonly IScriptAssemblyImageSource? _imageSource;
    private readonly ILogger _logger;

    public ReloadPipeline(
        HotReloadOptions options,
        ReloadableScriptContext context,
        ScriptInstanceRegistry registry,
        StatePreservationService statePreservation,
        IScriptSource? source,
        IScriptAssemblyImageSource? imageSource,
        ILogger logger)
    {
        _options = options;
        _context = context;
        _registry = registry;
        _statePreservation = statePreservation;
        _compiler = options.Compiler;
        _source = source;
        _imageSource = imageSource;
        _logger = logger;
    }

    public async Task<PipelineExecution> ExecuteAsync(
        ReloadTrigger trigger,
        string[] changedDocumentIds,
        bool fullRescan,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        IReadOnlyList<ScriptDiagnostic> diagnostics = [];

        try
        {
            // ---- Phase A: acquire the new image. No effect on the live generation. ----
            ScriptAssemblyImage image;
            if (_imageSource is not null)
            {
                var loadedImage = await _imageSource.LoadImageAsync(cancellationToken).ConfigureAwait(false);
                if (loadedImage is null)
                {
                    stopwatch.Stop();
                    if (_context.CurrentGeneration is null)
                    {
                        // Nothing published yet at startup: a valid empty state, not a failure.
                        var emptyResult = new ReloadResult(
                            ReloadOutcome.Succeeded, 0, diagnostics, false,
                            stopwatch.Elapsed, startedAt, DateTimeOffset.UtcNow);
                        return new PipelineExecution(emptyResult, 0, null);
                    }

                    Log.ImageSourceEmpty(_logger);
                    var diagnostic = PipelineDiagnostic(
                        "ESC0003", "The assembly image source returned no image; the current generation stays active.");
                    var failed = new ReloadResult(
                        ReloadOutcome.Faulted, _context.CurrentGeneration.Number, [diagnostic], false,
                        stopwatch.Elapsed, startedAt, DateTimeOffset.UtcNow);
                    return new PipelineExecution(failed, 0, null);
                }

                image = loadedImage;
            }
            else
            {
                await SynchronizeSourcesAsync(trigger, changedDocumentIds, fullRescan, cancellationToken).ConfigureAwait(false);

                var compilation = await _compiler!.CompileAsync(cancellationToken).ConfigureAwait(false);
                diagnostics = compilation.Diagnostics;
                if (!compilation.Success)
                {
                    stopwatch.Stop();
                    var failed = new ReloadResult(
                        ReloadOutcome.CompilationFailed, _context.CurrentGeneration?.Number ?? 0, diagnostics, false,
                        stopwatch.Elapsed, startedAt, DateTimeOffset.UtcNow);
                    return new PipelineExecution(failed, 0, null);
                }

                image = compilation.Image!;
            }

            // ---- Phase B: teardown of the old generation (only when one exists). ----
            UnloadResult? unloadResult = null;
            var retiredGenerationNumber = 0;
            Dictionary<Guid, ScriptStateSnapshot>? snapshots = null;
            if (_context.CurrentGeneration is not null)
            {
                retiredGenerationNumber = _context.CurrentGeneration.Number;
                snapshots = await CaptureAndTeardownAsync(cancellationToken).ConfigureAwait(false);
                unloadResult = await _context.UnloadCurrentGenerationAsync(cancellationToken).ConfigureAwait(false);
            }

            // ---- Phase C: bring up the new generation. ----
            var newGenerationNumber = await ActivateGenerationAsync(image, snapshots, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            var result = new ReloadResult(
                ReloadOutcome.Succeeded,
                newGenerationNumber,
                diagnostics,
                unloadResult?.Outcome == UnloadOutcome.TimedOut,
                stopwatch.Elapsed,
                startedAt,
                DateTimeOffset.UtcNow);
            return new PipelineExecution(result, retiredGenerationNumber, unloadResult);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            Log.PipelineFaulted(_logger, exception);

            var diagnostic = PipelineDiagnostic(
                "ESC0002", $"Reload pipeline failure ({exception.GetType().Name}): {exception.Message}");
            var result = new ReloadResult(
                ReloadOutcome.Faulted,
                _context.CurrentGeneration?.Number ?? 0,
                [diagnostic, .. diagnostics],
                false,
                stopwatch.Elapsed,
                startedAt,
                DateTimeOffset.UtcNow);
            return new PipelineExecution(result, 0, null, exception);
        }
    }

    private static ScriptDiagnostic PipelineDiagnostic(string id, string message)
        => new(id, ScriptDiagnosticSeverity.Error, message, DocumentId: null, Line: 0, Column: 0);

    private async Task SynchronizeSourcesAsync(
        ReloadTrigger trigger,
        string[] changedDocumentIds,
        bool fullRescan,
        CancellationToken cancellationToken)
    {
        if (fullRescan || trigger is ReloadTrigger.Initial or ReloadTrigger.Manual)
        {
            var documents = await _source!.LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var liveIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var document in documents)
            {
                liveIds.Add(document.DocumentId);
                _compiler!.AddOrUpdateSource(document.DocumentId, document.Content);
            }

            foreach (var knownId in _compiler!.DocumentIds)
            {
                if (!liveIds.Contains(knownId))
                {
                    _compiler.RemoveSource(knownId);
                }
            }

            return;
        }

        foreach (var documentId in changedDocumentIds)
        {
            var document = await _source!.LoadAsync(documentId, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                _compiler!.RemoveSource(documentId);
            }
            else
            {
                _compiler!.AddOrUpdateSource(document.DocumentId, document.Content);
            }
        }
    }

    /// <summary>
    /// Captures state, runs the before-hooks and detaches every instance — the last code that
    /// ever touches the old generation. All hoisted references are nulled before returning, so
    /// once this method completes, the only remaining root for the old context would be a
    /// consumer-held reference (which the unload verification then diagnoses).
    /// </summary>
    private async Task<Dictionary<Guid, ScriptStateSnapshot>> CaptureAndTeardownAsync(CancellationToken cancellationToken)
    {
        var entries = _registry.GetLiveEntries();
        var snapshots = new Dictionary<Guid, ScriptStateSnapshot>(entries.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            snapshots[entries[i].Key.Id] = _statePreservation.Capture(entries[i].Value);
            await InvokeBeforeReloadAsync(entries[i].Value, cancellationToken).ConfigureAwait(false);
        }

        _registry.DetachAll();
        entries = null!; // hoisted state-machine field: nulling it un-roots every old instance
        return snapshots;
    }

    private async Task InvokeBeforeReloadAsync(object? instance, CancellationToken cancellationToken)
    {
        if (instance is IReloadableScript script)
        {
            try
            {
                await script.OnBeforeReloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.BeforeReloadHookFailed(_logger, instance.GetType().FullName ?? "?", exception);
            }

            script = null!; // hoisted: see CaptureAndTeardownAsync
        }

        instance = null; // parameters are state-machine fields too
    }

    private async Task<int> ActivateGenerationAsync(
        ScriptAssemblyImage image,
        Dictionary<Guid, ScriptStateSnapshot>? snapshots,
        CancellationToken cancellationToken)
    {
        // Phase C only ever touches the NEW generation, which is supposed to stay alive — the
        // reference discipline of phase B does not apply here.
        var generation = _context.LoadGeneration(image);

        foreach (var handle in _registry.Handles)
        {
            var type = generation.Assembly.GetType(handle.TypeFullName, throwOnError: false);
            if (type is null)
            {
                Log.ScriptTypeMissing(_logger, handle.TypeFullName, handle.Id);
                _registry.Unregister(handle);
                continue;
            }

            object instance;
            try
            {
                instance = CreateInstance(type);
            }
            catch (Exception exception)
            {
                Log.ScriptActivationFailed(_logger, handle.TypeFullName, exception);
                _registry.Unregister(handle);
                continue;
            }

            if (snapshots is not null && snapshots.TryGetValue(handle.Id, out var snapshot))
            {
                _statePreservation.Restore(instance, snapshot);
            }

            _registry.Attach(handle, instance);
            await InvokeAfterReloadAsync(instance, cancellationToken).ConfigureAwait(false);
        }

        if (_options.ActivateAllReloadableScripts)
        {
            var knownTypeNames = _registry.Handles
                .Select(handle => handle.TypeFullName)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var type in ScriptActivator.GetActivatableScriptTypes(generation.Assembly, _logger))
            {
                var typeName = type.FullName ?? type.Name;
                if (knownTypeNames.Contains(typeName))
                {
                    continue;
                }

                try
                {
                    var instance = CreateInstance(type);
                    var handle = _registry.Register(typeName, instance);
                    Log.ScriptActivated(_logger, typeName, handle.Id);
                    await InvokeAfterReloadAsync(instance, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Log.ScriptActivationFailed(_logger, typeName, exception);
                }
            }
        }

        return generation.Number;
    }

    private async Task InvokeAfterReloadAsync(object instance, CancellationToken cancellationToken)
    {
        if (instance is IReloadableScript script)
        {
            try
            {
                await script.OnAfterReloadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                Log.AfterReloadHookFailed(_logger, instance.GetType().FullName ?? "?", exception);
            }
        }
    }

    private object CreateInstance(Type type)
    {
        var factory = _options.InstanceFactory;
        var instance = factory is not null ? factory(type) : Activator.CreateInstance(type);
        return instance ?? throw new InvalidOperationException($"The instance factory returned null for '{type.FullName}'.");
    }
}
