namespace Engine.Scripting.Sample.ConsoleDemo;

/// <summary>
/// Host-side contract the demo scripts implement. Handed to the script compiler via
/// <c>compilerOptions.AddReference(typeof(ITickable))</c> — the pattern any consumer uses to
/// expose its own contracts to scripts.
/// </summary>
public interface ITickable
{
    /// <summary>Advances the script by one tick and returns a printable status line.</summary>
    string Tick();
}
