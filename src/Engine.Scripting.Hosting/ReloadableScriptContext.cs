using System.Diagnostics;
using System.Runtime.CompilerServices;
using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Engine.Scripting.Hosting;

/// <summary>
/// Hosts one live script generation at a time inside a collectible
/// <c>AssemblyLoadContext</c>, and implements the Microsoft-recommended cooperative unload:
/// initiate <c>Unload()</c>, then verify actual collection through a <see cref="WeakReference"/>
/// probe under repeated GC cycles, bounded by a configurable timeout.
/// </summary>
/// <remarks>
/// <para>
/// Unloading in CoreCLR is <b>cooperative</b>: it only completes when no strong reference —
/// from a stack, a static field, a delegate, a GC handle — reaches anything loaded in the
/// collectible context. This class therefore never keeps a reference to a retired generation:
/// the fields are cleared inside a non-inlinable helper so not even a JIT-extended local keeps
/// the context alive during verification.
/// </para>
/// <para>
/// The class is not meant for concurrent mutation; the orchestration pipeline serializes calls.
/// Internal state is still guarded so misuse fails predictably rather than corrupting state.
/// </para>
/// </remarks>
public sealed class ReloadableScriptContext : IAsyncDisposable
{
    private readonly ScriptHostOptions _options;
    private readonly ILogger<ReloadableScriptContext> _logger;
    private readonly Lock _gate = new();
    private readonly List<WeakReference> _retiredProbes = [];
    private ScriptLoadContext? _currentLoadContext;
    private ScriptGeneration? _currentGeneration;
    private int _generationCounter;

    /// <summary>Creates the context.</summary>
    /// <param name="options">Unload verification options; defaults are used when omitted.</param>
    /// <param name="logger">Optional logger; defaults to a no-op logger.</param>
    public ReloadableScriptContext(ScriptHostOptions? options = null, ILogger<ReloadableScriptContext>? logger = null)
    {
        _options = options ?? new ScriptHostOptions();
        _logger = logger ?? NullLogger<ReloadableScriptContext>.Instance;
    }

    /// <summary>The live generation, or <see langword="null"/> when none is loaded.</summary>
    public ScriptGeneration? CurrentGeneration
    {
        get
        {
            lock (_gate)
            {
                return _currentGeneration;
            }
        }
    }

    /// <summary>
    /// One <see cref="WeakReference"/> probe per retired generation, in retirement order.
    /// After a successful unload (and a GC), the probe's <see cref="WeakReference.IsAlive"/>
    /// turns false — the memory-leak regression tests are built on exactly this.
    /// </summary>
    public IReadOnlyList<WeakReference> RetiredGenerationProbes
    {
        get
        {
            lock (_gate)
            {
                return [.. _retiredProbes];
            }
        }
    }

    /// <summary>
    /// Loads a new generation from an in-memory image into a fresh collectible load context.
    /// </summary>
    /// <param name="image">PE + optional PDB bytes; nothing is read from disk.</param>
    /// <returns>The loaded generation.</returns>
    /// <exception cref="InvalidOperationException">A generation is already loaded.</exception>
    public ScriptGeneration LoadGeneration(ScriptAssemblyImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        lock (_gate)
        {
            if (_currentGeneration is not null)
            {
                throw new InvalidOperationException(
                    $"Generation {_currentGeneration.Number} is still loaded. Unload it before loading a new one.");
            }

            var number = ++_generationCounter;
            var loadContext = new ScriptLoadContext(number);

            using var peStream = new MemoryStream(image.PeBytes, writable: false);
            using var pdbStream = image.PdbBytes is null ? null : new MemoryStream(image.PdbBytes, writable: false);
            var assembly = loadContext.LoadFromStream(peStream, pdbStream);

            _currentLoadContext = loadContext;
            _currentGeneration = new ScriptGeneration(number, assembly.GetName().Name ?? image.AssemblyName, assembly);

            Log.GenerationLoaded(_logger, number, _currentGeneration.AssemblyName);
            return _currentGeneration;
        }
    }

    /// <summary>
    /// Initiates the cooperative unload of the live generation and verifies actual collection:
    /// GC + finalizers in a loop, watching a <see cref="WeakReference"/> probe, until the probe
    /// dies or <see cref="ScriptHostOptions.UnloadTimeout"/> elapses.
    /// </summary>
    /// <remarks>
    /// A <see cref="UnloadOutcome.TimedOut"/> result is a diagnostic, not a failure: the unload
    /// stays pending inside the runtime and completes when the pinning reference disappears.
    /// </remarks>
    /// <param name="cancellationToken">Token that aborts the verification loop (not the unload itself).</param>
    public async Task<UnloadResult> UnloadCurrentGenerationAsync(CancellationToken cancellationToken = default)
    {
        var (probe, generationNumber) = InitiateUnload();
        if (probe is null)
        {
            return new UnloadResult(UnloadOutcome.NoGeneration, TimeSpan.Zero, 0);
        }

        Log.UnloadInitiated(_logger, generationNumber);

        var stopwatch = Stopwatch.StartNew();
        var gcCycles = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            gcCycles++;

            if (!probe.IsAlive)
            {
                stopwatch.Stop();
                Log.UnloadCollected(_logger, generationNumber, gcCycles, stopwatch.ElapsedMilliseconds);
                return new UnloadResult(UnloadOutcome.Collected, stopwatch.Elapsed, gcCycles);
            }

            if (stopwatch.Elapsed >= _options.UnloadTimeout)
            {
                stopwatch.Stop();
                Log.UnloadTimedOut(_logger, generationNumber, (long)_options.UnloadTimeout.TotalMilliseconds, gcCycles);
                return new UnloadResult(UnloadOutcome.TimedOut, stopwatch.Elapsed, gcCycles);
            }

            await Task.Delay(_options.UnloadPollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Unloads the live generation (if any) using the configured timeout.</summary>
    public async ValueTask DisposeAsync()
        => await UnloadCurrentGenerationAsync(CancellationToken.None).ConfigureAwait(false);

    /// <summary>
    /// Moves the live generation out of the fields and starts its unload, returning only a
    /// <see cref="WeakReference"/> probe.
    /// </summary>
    /// <remarks>
    /// <b>This method must not be inlined.</b> Its stack frame is the last place a strong
    /// reference to the load context lives; because the JIT may report locals as live until the
    /// end of a method (especially in Debug/tier-0), inlining this body into the verification
    /// loop above would keep the context reachable for the whole loop and turn every unload into
    /// a false timeout. As a standalone frame, everything dies at <c>return</c>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private (WeakReference? Probe, int GenerationNumber) InitiateUnload()
    {
        ScriptLoadContext? loadContext;
        int generationNumber;

        lock (_gate)
        {
            loadContext = _currentLoadContext;
            generationNumber = _currentGeneration?.Number ?? 0;
            _currentLoadContext = null;
            _currentGeneration = null;
        }

        if (loadContext is null)
        {
            return (null, 0);
        }

        var probe = new WeakReference(loadContext);
        lock (_gate)
        {
            _retiredProbes.Add(probe);
        }

        loadContext.Unload();
        return (probe, generationNumber);
    }
}
