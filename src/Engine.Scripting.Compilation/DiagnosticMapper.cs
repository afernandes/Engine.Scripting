using Engine.Scripting.Abstractions;
using Microsoft.CodeAnalysis;

namespace Engine.Scripting.Compilation;

/// <summary>
/// Maps Roslyn diagnostics onto the compiler-agnostic <see cref="ScriptDiagnostic"/> shape.
/// </summary>
internal static class DiagnosticMapper
{
    public static IReadOnlyList<ScriptDiagnostic> Map(IEnumerable<Diagnostic> diagnostics)
    {
        var mapped = new List<ScriptDiagnostic>();
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Hidden)
            {
                continue;
            }

            mapped.Add(Map(diagnostic));
        }

        return mapped;
    }

    private static ScriptDiagnostic Map(Diagnostic diagnostic)
    {
        string? documentId = null;
        var line = 0;
        var column = 0;

        var lineSpan = diagnostic.Location.GetLineSpan();
        if (lineSpan.IsValid)
        {
            documentId = string.IsNullOrEmpty(lineSpan.Path) ? null : lineSpan.Path;
            line = lineSpan.StartLinePosition.Line + 1;
            column = lineSpan.StartLinePosition.Character + 1;
        }

        return new ScriptDiagnostic(
            diagnostic.Id,
            MapSeverity(diagnostic.Severity),
            diagnostic.GetMessage(),
            documentId,
            line,
            column);
    }

    private static ScriptDiagnosticSeverity MapSeverity(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => ScriptDiagnosticSeverity.Error,
        DiagnosticSeverity.Warning => ScriptDiagnosticSeverity.Warning,
        DiagnosticSeverity.Info => ScriptDiagnosticSeverity.Info,
        _ => ScriptDiagnosticSeverity.Hidden,
    };
}
