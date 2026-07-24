namespace Engine.Scripting.Hosting;

/// <summary>
/// Configuration of the reloadable script host, chiefly the cooperative-unload verification.
/// </summary>
public sealed class ScriptHostOptions
{
    /// <summary>
    /// How long <see cref="ReloadableScriptContext.UnloadCurrentGenerationAsync"/> keeps
    /// verifying (GC + <see cref="WeakReference"/> probe) before reporting a timeout.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan UnloadTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Delay between verification cycles. Default: 100 milliseconds. Waits use
    /// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> — never a blocking sleep.
    /// </summary>
    public TimeSpan UnloadPollInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}
