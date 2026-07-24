namespace Engine.Scripting.Hosting;

/// <summary>Outcome of a cooperative unload attempt.</summary>
public enum UnloadOutcome
{
    /// <summary>There was no live generation to unload.</summary>
    NoGeneration = 0,

    /// <summary>The load context was verifiably collected (its <see cref="WeakReference"/> probe died).</summary>
    Collected = 1,

    /// <summary>
    /// The load context was still alive when the timeout elapsed — something (a static field, a
    /// delegate/closure, an event subscription, a serializer cache, an attached debugger or a
    /// thread still executing script code) holds a strong reference into it. The unload remains
    /// pending and completes on its own once the reference dies.
    /// </summary>
    TimedOut = 2,
}
