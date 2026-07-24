namespace Engine.Scripting.Abstractions;

/// <summary>
/// A structured compilation (or pipeline) diagnostic, decoupled from any compiler API.
/// </summary>
/// <param name="Id">
/// Diagnostic identifier — a compiler id such as <c>CS0103</c>, or a pipeline id such as
/// <c>ESC0001</c> (unexpected compilation failure).
/// </param>
/// <param name="Severity">Severity of the diagnostic.</param>
/// <param name="Message">Human-readable description.</param>
/// <param name="DocumentId">
/// Identifier of the document the diagnostic points at, or <see langword="null"/> when it has no
/// location.
/// </param>
/// <param name="Line">1-based line number, or 0 when the diagnostic has no location.</param>
/// <param name="Column">1-based column number, or 0 when the diagnostic has no location.</param>
public sealed record ScriptDiagnostic(
    string Id,
    ScriptDiagnosticSeverity Severity,
    string Message,
    string? DocumentId,
    int Line,
    int Column)
{
    /// <summary>Formats the diagnostic in a compiler-like, single-line form.</summary>
    public override string ToString()
        => DocumentId is null
            ? $"{Severity} {Id}: {Message}"
            : $"{DocumentId}({Line},{Column}): {Severity} {Id}: {Message}";
}
