namespace Engine.Scripting.Orchestration;

/// <summary>Payload of <see cref="HotReloadOrchestrator.ReloadStarted"/>.</summary>
public sealed class ReloadStartedEventArgs : EventArgs
{
    internal ReloadStartedEventArgs(ReloadTrigger trigger, IReadOnlyList<string> changedDocumentIds, DateTimeOffset timestampUtc)
    {
        Trigger = trigger;
        ChangedDocumentIds = changedDocumentIds;
        TimestampUtc = timestampUtc;
    }

    /// <summary>What initiated the reload.</summary>
    public ReloadTrigger Trigger { get; }

    /// <summary>Documents in the coalesced change batch (empty for initial/manual runs).</summary>
    public IReadOnlyList<string> ChangedDocumentIds { get; }

    /// <summary>UTC timestamp of the start.</summary>
    public DateTimeOffset TimestampUtc { get; }
}
