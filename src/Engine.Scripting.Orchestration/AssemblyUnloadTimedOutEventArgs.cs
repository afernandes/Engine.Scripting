namespace Engine.Scripting.Orchestration;

/// <summary>
/// Payload of <see cref="HotReloadOrchestrator.AssemblyUnloadTimedOut"/>. Carries only plain
/// values — deliberately nothing that could itself pin the load context it reports about.
/// </summary>
public sealed class AssemblyUnloadTimedOutEventArgs : EventArgs
{
    internal AssemblyUnloadTimedOutEventArgs(int generationNumber, TimeSpan timeout, TimeSpan elapsed, DateTimeOffset timestampUtc)
    {
        GenerationNumber = generationNumber;
        Timeout = timeout;
        Elapsed = elapsed;
        TimestampUtc = timestampUtc;
    }

    /// <summary>Number of the generation that failed to collect in time.</summary>
    public int GenerationNumber { get; }

    /// <summary>The configured unload timeout.</summary>
    public TimeSpan Timeout { get; }

    /// <summary>How long verification actually ran.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>UTC timestamp of the diagnosis.</summary>
    public DateTimeOffset TimestampUtc { get; }
}
