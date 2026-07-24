namespace Engine.Scripting.Orchestration.Sources;

/// <summary>
/// Thrown when a downloaded script assembly image fails integrity verification (SHA-256
/// mismatch, or an unusable checksum manifest).
/// </summary>
/// <remarks>
/// Integrity failures are never silently absorbed by cache fallbacks: a tampered or corrupted
/// artifact must surface loudly, and the reload pipeline reacts by keeping the current
/// generation active and reporting the failure.
/// </remarks>
public sealed class ScriptImageIntegrityException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Description of the integrity failure.</param>
    public ScriptImageIntegrityException(string message)
        : base(message)
    {
    }
}
