using Engine.Scripting.Hosting;

namespace Engine.Scripting.Orchestration;

/// <summary>
/// Internal outcome of one pipeline run: the public result plus the unload details the
/// orchestrator needs to raise <see cref="HotReloadOrchestrator.AssemblyUnloadTimedOut"/>.
/// </summary>
internal sealed record PipelineExecution(
    ReloadResult Result,
    int RetiredGenerationNumber,
    UnloadResult? Unload,
    Exception? Exception = null);
