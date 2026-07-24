using Engine.Scripting.Compilation;
using static Engine.Scripting.Orchestration.Tests.OrchestratorTestHelpers;

namespace Engine.Scripting.Orchestration.Tests;

/// <summary>
/// The one place where the real <see cref="FileSystemWatcher"/> behavior IS the subject:
/// rapid writes on disk must collapse into a single reload.
/// </summary>
[Collection("UnloadSensitive")]
public class WatcherDebounceTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Watcher_CincoGravacoesRapidasNoDisco_DisparaUmUnicoReload()
    {
        using var directory = new TempScriptDirectory();
        var scriptPath = directory.FilePath("counter.cs");
        await File.WriteAllTextAsync(scriptPath, ScriptSources.CounterScript("v1"), TestToken);

        var options = new HotReloadOptions
        {
            ScriptsPath = directory.Path,
            Compiler = new IncrementalScriptCompiler(CreateCompilerOptions()),
            EnableSourceWatching = true,
            DebounceInterval = TimeSpan.FromMilliseconds(250),
        };
        ApplyFastUnload(options.Hosting);

        await using var orchestrator = new HotReloadOrchestrator(options);
        var startedRecorder = new EventRecorder<ReloadStartedEventArgs>();
        var succeededRecorder = new EventRecorder<ReloadSucceededEventArgs>();
        orchestrator.ReloadStarted += (_, e) => startedRecorder.Record(e);
        orchestrator.ReloadSucceeded += (_, e) => succeededRecorder.Record(e);

        await orchestrator.StartAsync(TestToken);
        Assert.Equal(1, startedRecorder.Count); // initial load
        var handle = Assert.Single(orchestrator.Registry.Handles);
        IncrementScript(orchestrator.Registry, handle);

        for (var revision = 1; revision <= 5; revision++)
        {
            await File.WriteAllTextAsync(scriptPath, ScriptSources.CounterScript($"v2rev{revision}"), TestToken);
            await Task.Delay(TimeSpan.FromMilliseconds(30), TestToken); // within the debounce window
        }

        await succeededRecorder.WaitForCountAsync(2, TimeSpan.FromSeconds(15), TestToken);

        // A quiet period 3x the debounce long must not produce any extra reload.
        await Task.Delay(TimeSpan.FromMilliseconds(750), TestToken);

        Assert.Equal(2, startedRecorder.Count);
        Assert.Equal("v2rev5:1", DescribeScript(orchestrator.Registry, handle));
    }
}
