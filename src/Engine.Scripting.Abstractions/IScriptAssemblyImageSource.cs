namespace Engine.Scripting.Abstractions;

/// <summary>
/// Pluggable origin of a <b>precompiled</b> script assembly image — the production-mode
/// counterpart of <see cref="IScriptSource"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the deployment model for hosts that must not carry a compiler (mobile devices,
/// lean servers, ERP-style "compile once, distribute the binary" flows): a build step compiles
/// the scripts once, and the running hosts merely load the resulting image and hot-swap it when
/// a new version is published. The reload pipeline (snapshot, cooperative unload, restore,
/// lifecycle hooks) behaves exactly as in source mode.
/// </para>
/// <para>
/// When the source is injected into the orchestrator options, its lifetime belongs to the caller:
/// the orchestrator never disposes a source it did not create.
/// </para>
/// </remarks>
public interface IScriptAssemblyImageSource : IAsyncDisposable
{
    /// <summary>Raised when a new image version is published. May fire from any thread.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Loads the current image, or returns <see langword="null"/> when nothing has been
    /// published yet.
    /// </summary>
    /// <param name="cancellationToken">Token that aborts the read.</param>
    Task<ScriptAssemblyImage?> LoadImageAsync(CancellationToken cancellationToken);

    /// <summary>Starts emitting <see cref="Changed"/> notifications.</summary>
    /// <param name="cancellationToken">Token that aborts the startup.</param>
    Task StartWatchingAsync(CancellationToken cancellationToken);

    /// <summary>Stops emitting <see cref="Changed"/> notifications.</summary>
    /// <param name="cancellationToken">Token that aborts the shutdown.</param>
    Task StopWatchingAsync(CancellationToken cancellationToken);
}
