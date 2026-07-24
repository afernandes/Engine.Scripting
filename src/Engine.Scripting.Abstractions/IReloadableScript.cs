namespace Engine.Scripting.Abstractions;

/// <summary>
/// Contract implemented by hot-reloadable scripts that need lifecycle hooks around a reload.
/// </summary>
/// <remarks>
/// Implementing this interface is what makes a concrete script type eligible for automatic
/// activation by the orchestrator. Both hooks are optional in spirit — implementations that have
/// nothing to do should simply return <see cref="ValueTask.CompletedTask"/>.
/// </remarks>
public interface IReloadableScript
{
    /// <summary>
    /// Called on the <b>old</b> instance right before its generation is unloaded.
    /// </summary>
    /// <remarks>
    /// This is the last moment the script is fully functional, and the only chance to release
    /// everything that would otherwise keep the collectible <c>AssemblyLoadContext</c> alive:
    /// unsubscribe from host events, cancel timers and background tasks, dispose native handles.
    /// A script that skips this cleanup is the classic cause of an
    /// <c>AssemblyUnloadTimedOut</c> diagnostic — the unload is cooperative and cannot be forced.
    /// </remarks>
    /// <param name="cancellationToken">Token that aborts the reload pipeline.</param>
    ValueTask OnBeforeReloadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Called on the <b>new</b> instance after the new generation is loaded and the preserved
    /// state has been restored.
    /// </summary>
    /// <remarks>
    /// Use it to re-acquire resources released in <see cref="OnBeforeReloadAsync"/> and to
    /// revalidate restored state (members may have been discarded if their type or name changed).
    /// </remarks>
    /// <param name="cancellationToken">Token that aborts the reload pipeline.</param>
    ValueTask OnAfterReloadAsync(CancellationToken cancellationToken);
}
