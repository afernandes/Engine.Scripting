namespace Engine.Scripting.Orchestration;

/// <summary>What initiated a reload pipeline run.</summary>
public enum ReloadTrigger
{
    /// <summary>The initial load performed by <see cref="HotReloadOrchestrator.StartAsync"/>.</summary>
    Initial = 0,

    /// <summary>A change notification from the script (or assembly-image) source, after debouncing.</summary>
    SourceChange = 1,

    /// <summary>An explicit call to <see cref="HotReloadOrchestrator.ReloadAsync"/>.</summary>
    Manual = 2,
}
