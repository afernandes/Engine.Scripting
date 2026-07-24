namespace Engine.Scripting.Sample.ConsoleDemo;

/// <summary>Seeds the initial editable script so the demo works out of the box.</summary>
public static class InitialScriptSeeder
{
    private const string SeedScript = """
        using System.Threading;
        using System.Threading.Tasks;
        using Engine.Scripting.Abstractions;
        using Engine.Scripting.Sample.ConsoleDemo;

        public class CounterScript : IReloadableScript, ITickable
        {
            // >>> EDIT ME while the demo is running, then save: <<<
            // the message below changes instantly, but _ticks survives the reload.
            private const string Message = "Hello from generation ONE";

            [HotReloadState]
            private int _ticks;

            public string Tick()
            {
                _ticks++;
                return $"{Message} | ticks so far: {_ticks}";
            }

            public ValueTask OnBeforeReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask OnAfterReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }
        """;

    /// <summary>Creates <c>CounterScript.cs</c> under <paramref name="scriptsDirectory"/> if missing.</summary>
    /// <param name="scriptsDirectory">Directory watched by the demo.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>The full path of the script file.</returns>
    public static async Task<string> EnsureSeedScriptAsync(string scriptsDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scriptsDirectory);
        var scriptPath = Path.Combine(scriptsDirectory, "CounterScript.cs");
        if (!File.Exists(scriptPath))
        {
            await File.WriteAllTextAsync(scriptPath, SeedScript, cancellationToken);
        }

        return scriptPath;
    }
}
