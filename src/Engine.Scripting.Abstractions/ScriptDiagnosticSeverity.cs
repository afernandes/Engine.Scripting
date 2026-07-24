namespace Engine.Scripting.Abstractions;

/// <summary>
/// Severity of a <see cref="ScriptDiagnostic"/>, mirroring compiler severities without exposing
/// compiler types.
/// </summary>
public enum ScriptDiagnosticSeverity
{
    /// <summary>Diagnostic that is not surfaced to the user by default.</summary>
    Hidden = 0,

    /// <summary>Informational message.</summary>
    Info = 1,

    /// <summary>Warning that does not prevent the reload from being applied.</summary>
    Warning = 2,

    /// <summary>Error that prevents the new generation from being produced.</summary>
    Error = 3,
}
