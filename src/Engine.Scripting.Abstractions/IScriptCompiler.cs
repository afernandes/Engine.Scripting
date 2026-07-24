namespace Engine.Scripting.Abstractions;

/// <summary>
/// Incremental script compiler abstraction: a mutable set of source documents that can be
/// compiled into an in-memory assembly image on demand.
/// </summary>
/// <remarks>
/// This interface deliberately lives in the abstractions package so orchestration components can
/// depend on it without referencing a real compiler — production deployments that consume
/// precompiled images never load one.
/// </remarks>
public interface IScriptCompiler
{
    /// <summary>Number of source documents currently held by the compiler.</summary>
    int SourceCount { get; }

    /// <summary>
    /// Snapshot of the identifiers of every document currently held by the compiler — used by
    /// orchestration to reconcile removals during a full rescan.
    /// </summary>
    IReadOnlyCollection<string> DocumentIds { get; }

    /// <summary>
    /// Adds a new document or replaces the content of an existing one. Implementations reparse
    /// only the affected document.
    /// </summary>
    /// <param name="documentId">Stable identifier of the document (see <see cref="ScriptDocument.DocumentId"/>).</param>
    /// <param name="sourceText">Full C# source text of the document.</param>
    void AddOrUpdateSource(string documentId, string sourceText);

    /// <summary>Removes a document from the compilation.</summary>
    /// <param name="documentId">Stable identifier of the document.</param>
    /// <returns><see langword="true"/> when the document existed.</returns>
    bool RemoveSource(string documentId);

    /// <summary>
    /// Compiles the current document set into an in-memory image.
    /// </summary>
    /// <remarks>
    /// Never throws for compilation problems — failures are reported through
    /// <see cref="ScriptCompilationResult"/>. The only exception that escapes is
    /// <see cref="OperationCanceledException"/> when <paramref name="cancellationToken"/> fires.
    /// </remarks>
    /// <param name="cancellationToken">Token that aborts the compilation.</param>
    Task<ScriptCompilationResult> CompileAsync(CancellationToken cancellationToken = default);
}
