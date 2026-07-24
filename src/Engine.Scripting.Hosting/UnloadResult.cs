namespace Engine.Scripting.Hosting;

/// <summary>
/// Result of <see cref="ReloadableScriptContext.UnloadCurrentGenerationAsync"/>.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Elapsed">Wall-clock time spent verifying.</param>
/// <param name="GcCycles">Number of GC + finalizer verification cycles executed.</param>
public sealed record UnloadResult(UnloadOutcome Outcome, TimeSpan Elapsed, int GcCycles);
