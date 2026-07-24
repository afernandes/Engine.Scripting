namespace Engine.Scripting.Orchestration;

/// <summary>
/// Thread-safe accumulator of change notifications between pipeline runs: document ids are
/// coalesced into a set, and a full-rescan request subsumes everything.
/// </summary>
internal sealed class PendingChangeSet
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _documentIds = new(StringComparer.Ordinal);
    private bool _fullRescan;

    public void Merge(IReadOnlyList<string> documentIds, bool requiresFullRescan)
    {
        lock (_gate)
        {
            if (requiresFullRescan)
            {
                _fullRescan = true;
                return;
            }

            foreach (var documentId in documentIds)
            {
                _documentIds.Add(documentId);
            }
        }
    }

    public void RequestFullRescan()
    {
        lock (_gate)
        {
            _fullRescan = true;
        }
    }

    /// <summary>Atomically takes everything accumulated so far and resets the set.</summary>
    public (string[] DocumentIds, bool FullRescan) Drain()
    {
        lock (_gate)
        {
            var ids = _documentIds.ToArray();
            var fullRescan = _fullRescan;
            _documentIds.Clear();
            _fullRescan = false;
            return (ids, fullRescan);
        }
    }
}
