namespace Engine.Scripting.Compilation;

/// <summary>
/// Thrown when the compiler is configured in a way that can never work — for example adding a
/// reference to an assembly that has no file location in a single-file host.
/// </summary>
/// <remarks>
/// Configuration errors are deliberately loud (an exception at setup time) while compilation
/// errors are deliberately quiet (diagnostics in <see cref="Abstractions.ScriptCompilationResult"/>):
/// the former is a host bug, the latter is a normal consequence of editing scripts.
/// </remarks>
public sealed class ScriptingConfigurationException : Exception
{
    /// <summary>Creates the exception with a message explaining how to fix the configuration.</summary>
    /// <param name="message">Description of the configuration problem.</param>
    public ScriptingConfigurationException(string message)
        : base(message)
    {
    }
}
