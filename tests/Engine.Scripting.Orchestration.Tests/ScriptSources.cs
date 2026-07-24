namespace Engine.Scripting.Orchestration.Tests;

/// <summary>C# sources used as script fixtures.</summary>
internal static class ScriptSources
{
    /// <summary>
    /// A counter script whose <c>Describe()</c> output is tagged with <paramref name="versionTag"/>,
    /// so tests can tell which script version is running while the counter survives reloads.
    /// </summary>
    public static string CounterScript(string versionTag) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Engine.Scripting.Abstractions;

        public class CounterScript : IReloadableScript, Engine.Scripting.Orchestration.Tests.ICounterScript
        {
            [HotReloadState]
            private int _count;

            public int Increment() => ++_count;

            public int GetCount() => _count;

            public string Describe() => "{{versionTag}}:" + _count;

            public ValueTask OnBeforeReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask OnAfterReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }
        """;

    /// <summary>A second, independent reloadable script type.</summary>
    public const string SecondScript = """
        using System.Threading;
        using System.Threading.Tasks;
        using Engine.Scripting.Abstractions;

        public class SecondScript : IReloadableScript
        {
            [HotReloadState]
            public int Ticks { get; set; }

            public ValueTask OnBeforeReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask OnAfterReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }
        """;

    /// <summary>A script that does not compile.</summary>
    public const string BrokenScript = """
        public class CounterScript
        {
            public int Increment() => ;
        }
        """;
}
