namespace Engine.Scripting.Extensions.Hosting.Tests;

/// <summary>Host-side contract the DI test scripts implement.</summary>
public interface IGreeterScript
{
    /// <summary>Returns a line combining script version, injected data and preserved state.</summary>
    string Greet();
}

/// <summary>Host service injected into script constructors by the container.</summary>
public interface IGreetingProvider
{
    /// <summary>The greeting text.</summary>
    string GetGreeting();
}

/// <summary>Deterministic implementation registered in the test containers.</summary>
public sealed class TestGreetingProvider : IGreetingProvider
{
    /// <inheritdoc />
    public string GetGreeting() => "from-container";
}
