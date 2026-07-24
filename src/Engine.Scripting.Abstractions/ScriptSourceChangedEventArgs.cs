namespace Engine.Scripting.Abstractions;

/// <summary>
/// Payload of <see cref="IScriptSource.Changed"/>: which documents changed, or whether the whole
/// source must be rescanned.
/// </summary>
public sealed class ScriptSourceChangedEventArgs : EventArgs
{
    /// <summary>
    /// Creates the event payload.
    /// </summary>
    /// <param name="changedDocumentIds">
    /// Identifiers of documents that were added, updated or removed. The consumer distinguishes
    /// removal by <see cref="IScriptSource.LoadAsync"/> returning <see langword="null"/>.
    /// </param>
    /// <param name="requiresFullRescan">
    /// When <see langword="true"/>, change tracking was lost (for example a watcher buffer
    /// overflow) and the consumer should reload every document via
    /// <see cref="IScriptSource.LoadAllAsync"/>.
    /// </param>
    public ScriptSourceChangedEventArgs(IReadOnlyList<string> changedDocumentIds, bool requiresFullRescan = false)
    {
        ArgumentNullException.ThrowIfNull(changedDocumentIds);
        ChangedDocumentIds = changedDocumentIds;
        RequiresFullRescan = requiresFullRescan;
    }

    /// <summary>Identifiers of the documents that were added, updated or removed.</summary>
    public IReadOnlyList<string> ChangedDocumentIds { get; }

    /// <summary>Indicates that the consumer should discard incremental state and rescan everything.</summary>
    public bool RequiresFullRescan { get; }
}
