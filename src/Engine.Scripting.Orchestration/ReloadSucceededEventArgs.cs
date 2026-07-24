namespace Engine.Scripting.Orchestration;

/// <summary>Payload of <see cref="HotReloadOrchestrator.ReloadSucceeded"/>.</summary>
public sealed class ReloadSucceededEventArgs : EventArgs
{
    internal ReloadSucceededEventArgs(ReloadResult result)
    {
        Result = result;
    }

    /// <summary>The successful run.</summary>
    public ReloadResult Result { get; }
}
