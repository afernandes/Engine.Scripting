namespace Engine.Scripting.Abstractions;

/// <summary>
/// Pluggable origin of script <b>source text</b>: a directory of <c>.cs</c> files, a database
/// table, a remote service — anything that can enumerate documents and signal changes.
/// </summary>
/// <remarks>
/// <para>
/// Implementations must not debounce or coalesce <see cref="Changed"/> notifications; the
/// orchestrator owns debouncing so the behavior is uniform across sources (file-watcher bursts
/// and database NOTIFY storms alike).
/// </para>
/// <para>
/// When the source is injected into the orchestrator options, its lifetime belongs to the caller:
/// the orchestrator never disposes a source it did not create.
/// </para>
/// </remarks>
public interface IScriptSource : IAsyncDisposable
{
    /// <summary>
    /// Raised when one or more documents change. May fire from any thread, and must not be
    /// raised with heavy work on the caller's stack.
    /// </summary>
    event EventHandler<ScriptSourceChangedEventArgs>? Changed;

    /// <summary>Loads every document currently available in the source.</summary>
    /// <param name="cancellationToken">Token that aborts the read.</param>
    Task<IReadOnlyList<ScriptDocument>> LoadAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Loads a single document, or returns <see langword="null"/> when the document no longer
    /// exists (which the consumer treats as a removal).
    /// </summary>
    /// <param name="documentId">Identifier previously announced by the source.</param>
    /// <param name="cancellationToken">Token that aborts the read.</param>
    Task<ScriptDocument?> LoadAsync(string documentId, CancellationToken cancellationToken);

    /// <summary>Starts emitting <see cref="Changed"/> notifications.</summary>
    /// <param name="cancellationToken">Token that aborts the startup.</param>
    Task StartWatchingAsync(CancellationToken cancellationToken);

    /// <summary>Stops emitting <see cref="Changed"/> notifications.</summary>
    /// <param name="cancellationToken">Token that aborts the shutdown.</param>
    Task StopWatchingAsync(CancellationToken cancellationToken);
}
