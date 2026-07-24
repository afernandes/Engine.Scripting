using Engine.Scripting.Abstractions;

namespace Engine.Scripting.Orchestration.Sources;

/// <summary>
/// In-memory <see cref="IScriptSource"/>: documents live in a dictionary and every mutation
/// raises <see cref="Changed"/>.
/// </summary>
/// <remarks>
/// Serves two purposes: it is the minimal reference implementation of a custom source (a
/// database-backed source follows exactly this shape — load rows, raise <see cref="Changed"/>
/// on notification), and it makes orchestrator tests deterministic by removing file-system
/// timing from the equation.
/// </remarks>
public sealed class InMemoryScriptSource : IScriptSource
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event EventHandler<ScriptSourceChangedEventArgs>? Changed;

    /// <summary>Adds or replaces a document and raises <see cref="Changed"/>.</summary>
    /// <param name="documentId">Stable identifier of the document.</param>
    /// <param name="content">Full C# source text.</param>
    public void SetScript(string documentId, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        ArgumentNullException.ThrowIfNull(content);

        lock (_gate)
        {
            _documents[documentId] = content;
        }

        Changed?.Invoke(this, new ScriptSourceChangedEventArgs([documentId]));
    }

    /// <summary>Removes a document and raises <see cref="Changed"/> when it existed.</summary>
    /// <param name="documentId">Stable identifier of the document.</param>
    /// <returns><see langword="true"/> when the document existed.</returns>
    public bool RemoveScript(string documentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);

        bool removed;
        lock (_gate)
        {
            removed = _documents.Remove(documentId);
        }

        if (removed)
        {
            Changed?.Invoke(this, new ScriptSourceChangedEventArgs([documentId]));
        }

        return removed;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScriptDocument>> LoadAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<ScriptDocument> snapshot =
                [.. _documents.Select(pair => new ScriptDocument(pair.Key, pair.Value))];
            return Task.FromResult(snapshot);
        }
    }

    /// <inheritdoc />
    public Task<ScriptDocument?> LoadAsync(string documentId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_documents.TryGetValue(documentId, out var content)
                ? new ScriptDocument(documentId, content)
                : null);
        }
    }

    /// <inheritdoc />
    public Task StartWatchingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopWatchingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
