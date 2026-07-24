using Engine.Scripting.Abstractions;

namespace Engine.Scripting.Orchestration;

/// <summary>Complete description of one reload pipeline run.</summary>
/// <param name="Outcome">How the run ended.</param>
/// <param name="Generation">Number of the generation active after the run (0 when none).</param>
/// <param name="Diagnostics">Compiler/pipeline diagnostics collected during the run.</param>
/// <param name="UnloadTimedOut">
/// Whether the previous generation failed to collect within the configured timeout. The reload
/// still proceeds — the leak stays bounded to that one generation and resolves itself once the
/// pinning reference dies.
/// </param>
/// <param name="Duration">Wall-clock duration of the run.</param>
/// <param name="StartedAtUtc">UTC timestamp at the start of the run.</param>
/// <param name="CompletedAtUtc">UTC timestamp at the end of the run.</param>
public sealed record ReloadResult(
    ReloadOutcome Outcome,
    int Generation,
    IReadOnlyList<ScriptDiagnostic> Diagnostics,
    bool UnloadTimedOut,
    TimeSpan Duration,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc);
