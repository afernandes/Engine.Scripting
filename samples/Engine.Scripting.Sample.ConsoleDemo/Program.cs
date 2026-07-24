using Engine.Scripting.Abstractions;
using Engine.Scripting.Compilation;
using Engine.Scripting.Orchestration;
using Engine.Scripting.Sample.ConsoleDemo;
using Microsoft.Extensions.Logging;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdown.Cancel();
};

var scriptsDirectory = Path.GetFullPath("scripts");
var scriptPath = await InitialScriptSeeder.EnsureSeedScriptAsync(scriptsDirectory, shutdown.Token);

using var loggerFactory = LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Information)
    .AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.TimestampFormat = "HH:mm:ss.fff ";
        console.UseUtcTimestamp = true;
    }));

var compilerOptions = new ScriptCompilerOptions();
compilerOptions.AddReference(typeof(ITickable));          // host contract for the scripts
compilerOptions.AddReference(typeof(IReloadableScript));  // lifecycle hooks + [HotReloadState]

var options = new HotReloadOptions
{
    ScriptsPath = scriptsDirectory,
    Compiler = new IncrementalScriptCompiler(compilerOptions, loggerFactory.CreateLogger<IncrementalScriptCompiler>()),
    DebounceInterval = TimeSpan.FromMilliseconds(250),
};

await using var orchestrator = new HotReloadOrchestrator(options, loggerFactory);

orchestrator.ReloadStarted += (_, e) =>
    WriteEvent(ConsoleColor.Cyan, $"[reload started] trigger={e.Trigger} changed={e.ChangedDocumentIds.Count} at {e.TimestampUtc:O}");

orchestrator.ReloadSucceeded += (_, e) =>
    WriteEvent(ConsoleColor.Green,
        $"[reload succeeded] generation={e.Result.Generation} in {e.Result.Duration.TotalMilliseconds:F0} ms " +
        $"(unload timed out: {e.Result.UnloadTimedOut}) at {e.Result.CompletedAtUtc:O}");

orchestrator.ReloadFailed += (_, e) =>
{
    WriteEvent(ConsoleColor.Red, $"[reload FAILED] outcome={e.Outcome} at {e.TimestampUtc:O} — previous generation stays active");
    foreach (var diagnostic in e.Diagnostics.Where(d => d.Severity == ScriptDiagnosticSeverity.Error))
    {
        WriteEvent(ConsoleColor.Red, $"    {diagnostic}");
    }
};

orchestrator.AssemblyUnloadTimedOut += (_, e) =>
    WriteEvent(ConsoleColor.Yellow,
        $"[unload timed out] generation={e.GenerationNumber} after {e.Elapsed.TotalMilliseconds:F0} ms — something still pins the old ALC");

Console.WriteLine();
Console.WriteLine("Engine.Scripting — hot-reload console demo");
Console.WriteLine("===========================================");
Console.WriteLine($"  Scripts directory : {scriptsDirectory}");
Console.WriteLine($"  Try it            : edit '{Path.GetFileName(scriptPath)}' (change the Message constant), save, watch the reload.");
Console.WriteLine("  What to observe   : the message changes, the tick counter marked [HotReloadState] survives.");
Console.WriteLine("  Break it          : save a syntax error — the reload fails and the old script keeps running.");
Console.WriteLine("  Debug it          : attach your debugger to this process and set a breakpoint inside the script file.");
Console.WriteLine("  Exit              : Ctrl+C");
Console.WriteLine();

await orchestrator.StartAsync(shutdown.Token);

try
{
    while (!shutdown.Token.IsCancellationRequested)
    {
        foreach (var handle in orchestrator.Registry.Handles)
        {
            // Resolve on every use — never cache the instance across reloads.
            var output = orchestrator.Registry.GetAs<ITickable>(handle)?.Tick();
            if (output is not null)
            {
                Console.WriteLine($"  [gen {orchestrator.CurrentGenerationNumber}] {output}");
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(1), shutdown.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C — fall through to the graceful dispose.
}

Console.WriteLine("Shutting down…");

static void WriteEvent(ConsoleColor color, string message)
{
    var previous = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(message);
    Console.ForegroundColor = previous;
}
