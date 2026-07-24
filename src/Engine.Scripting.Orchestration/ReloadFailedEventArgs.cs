using Engine.Scripting.Abstractions;

namespace Engine.Scripting.Orchestration;

/// <summary>Payload of <see cref="HotReloadOrchestrator.ReloadFailed"/>.</summary>
public sealed class ReloadFailedEventArgs : EventArgs
{
    internal ReloadFailedEventArgs(ReloadOutcome outcome, IReadOnlyList<ScriptDiagnostic> diagnostics, Exception? exception, DateTimeOffset timestampUtc)
    {
        Outcome = outcome;
        Diagnostics = diagnostics;
        Exception = exception;
        TimestampUtc = timestampUtc;
    }

    /// <summary>Why the reload did not apply (compilation failure or pipeline fault).</summary>
    public ReloadOutcome Outcome { get; }

    /// <summary>Structured diagnostics (compilation errors, pipeline error codes).</summary>
    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; }

    /// <summary>The pipeline exception when <see cref="Outcome"/> is <see cref="ReloadOutcome.Faulted"/>.</summary>
    public Exception? Exception { get; }

    /// <summary>UTC timestamp of the failure.</summary>
    public DateTimeOffset TimestampUtc { get; }
}
