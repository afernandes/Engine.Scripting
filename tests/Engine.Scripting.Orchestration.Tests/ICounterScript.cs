namespace Engine.Scripting.Orchestration.Tests;

/// <summary>
/// Host-side contract implemented by the test scripts — proves that scripts can implement
/// consumer interfaces (the test assembly is added as a compiler reference).
/// </summary>
public interface ICounterScript
{
    /// <summary>Increments and returns the preserved counter.</summary>
    int Increment();

    /// <summary>Returns the preserved counter.</summary>
    int GetCount();

    /// <summary>Returns a version-tagged description, e.g. <c>v1:3</c>.</summary>
    string Describe();
}
