namespace Engine.Scripting.Compilation.Tests.HostContracts;

/// <summary>
/// Host-side contract used to prove that consumer-supplied assembly references let scripts
/// implement host interfaces.
/// </summary>
public interface IHostContract
{
    /// <summary>Any value, produced by the script.</summary>
    int GetValue();
}
