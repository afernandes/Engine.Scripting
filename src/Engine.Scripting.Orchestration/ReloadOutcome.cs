namespace Engine.Scripting.Orchestration;

/// <summary>Outcome of one reload pipeline run.</summary>
public enum ReloadOutcome
{
    /// <summary>The new generation is loaded and active.</summary>
    Succeeded = 0,

    /// <summary>
    /// Compilation reported errors; the previous generation is untouched and stays active.
    /// </summary>
    CompilationFailed = 1,

    /// <summary>An unexpected failure occurred; see the diagnostics and exception.</summary>
    Faulted = 2,

    /// <summary>The run was cancelled (typically host shutdown). No events are raised.</summary>
    Cancelled = 3,
}
