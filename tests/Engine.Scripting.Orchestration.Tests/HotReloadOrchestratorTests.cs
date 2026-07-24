using System.Runtime.CompilerServices;
using Engine.Scripting.Abstractions;
using Engine.Scripting.Instances;
using Engine.Scripting.Orchestration.Sources;
using static Engine.Scripting.Orchestration.Tests.OrchestratorTestHelpers;

namespace Engine.Scripting.Orchestration.Tests;

[Collection("UnloadSensitive")]
public class HotReloadOrchestratorTests
{
    private static object? s_pinnedInstance;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReloadAsync_CampoComHotReloadState_PreservaValorAposReload()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("counter.cs", ScriptSources.CounterScript("v1"));
        await using var orchestrator = new HotReloadOrchestrator(CreateInMemoryOptions(source));

        await orchestrator.StartAsync(TestToken);
        var handle = Assert.Single(orchestrator.Registry.Handles);

        IncrementScript(orchestrator.Registry, handle);
        IncrementScript(orchestrator.Registry, handle);
        IncrementScript(orchestrator.Registry, handle);

        source.SetScript("counter.cs", ScriptSources.CounterScript("v2"));
        var result = await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
        Assert.False(result.UnloadTimedOut);
        Assert.Equal(2, orchestrator.CurrentGenerationNumber);
        Assert.Equal(handle, Assert.Single(orchestrator.Registry.Handles));
        Assert.Equal("v2:3", DescribeScript(orchestrator.Registry, handle));
        Assert.True(result.CompletedAtUtc >= result.StartedAtUtc);
    }

    [Fact]
    public async Task ReloadAsync_ErroDeCompilacao_MantemGeracaoAnteriorEEmiteDiagnosticos()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("counter.cs", ScriptSources.CounterScript("v1"));
        await using var orchestrator = new HotReloadOrchestrator(CreateInMemoryOptions(source));
        var failedRecorder = new EventRecorder<ReloadFailedEventArgs>();
        orchestrator.ReloadFailed += (_, e) => failedRecorder.Record(e);

        await orchestrator.StartAsync(TestToken);
        var handle = Assert.Single(orchestrator.Registry.Handles);
        IncrementScript(orchestrator.Registry, handle);
        IncrementScript(orchestrator.Registry, handle);

        source.SetScript("counter.cs", ScriptSources.BrokenScript);
        var brokenResult = await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(ReloadOutcome.CompilationFailed, brokenResult.Outcome);
        Assert.Contains(brokenResult.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error);
        Assert.Equal(1, orchestrator.CurrentGenerationNumber);
        Assert.Equal("v1:2", DescribeScript(orchestrator.Registry, handle));

        var failedEvent = Assert.Single(failedRecorder.Snapshot);
        Assert.Equal(ReloadOutcome.CompilationFailed, failedEvent.Outcome);
        Assert.Contains(failedEvent.Diagnostics, d => d.Severity == ScriptDiagnosticSeverity.Error);

        // Fixing the script must recover, still preserving the counter.
        source.SetScript("counter.cs", ScriptSources.CounterScript("v2"));
        var fixedResult = await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(ReloadOutcome.Succeeded, fixedResult.Outcome);
        Assert.Equal("v2:2", DescribeScript(orchestrator.Registry, handle));
    }

    [Fact]
    public async Task ReloadAsync_DezCiclosConsecutivos_NaoAcumulaAlcsVivos()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("counter.cs", ScriptSources.CounterScript("gen0"));
        await using var orchestrator = new HotReloadOrchestrator(CreateInMemoryOptions(source));

        await orchestrator.StartAsync(TestToken);
        var handle = Assert.Single(orchestrator.Registry.Handles);

        for (var cycle = 1; cycle <= 10; cycle++)
        {
            IncrementScript(orchestrator.Registry, handle);
            source.SetScript("counter.cs", ScriptSources.CounterScript($"gen{cycle}"));

            var result = await orchestrator.ReloadAsync(TestToken);

            Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
            Assert.False(result.UnloadTimedOut, $"unexpected unload timeout on cycle {cycle}");
        }

        Assert.Equal("gen10:10", DescribeScript(orchestrator.Registry, handle));

        var probes = orchestrator.RetiredGenerationProbes;
        Assert.Equal(10, probes.Count);
        foreach (var probe in probes)
        {
            ForceCollect(probe);
            Assert.False(probe.IsAlive, "a retired generation is still reachable — memory would grow unbounded");
        }
    }

    [Fact]
    public async Task Source_CincoNotificacoesRapidas_DisparaUmUnicoReload()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("counter.cs", ScriptSources.CounterScript("v1"));
        var options = CreateInMemoryOptions(source, enableWatching: true, debounceInterval: TimeSpan.FromMilliseconds(200));
        await using var orchestrator = new HotReloadOrchestrator(options);
        var startedRecorder = new EventRecorder<ReloadStartedEventArgs>();
        var succeededRecorder = new EventRecorder<ReloadSucceededEventArgs>();
        orchestrator.ReloadStarted += (_, e) => startedRecorder.Record(e);
        orchestrator.ReloadSucceeded += (_, e) => succeededRecorder.Record(e);

        await orchestrator.StartAsync(TestToken);
        Assert.Equal(1, startedRecorder.Count); // initial load

        for (var revision = 1; revision <= 5; revision++)
        {
            source.SetScript("counter.cs", ScriptSources.CounterScript($"v2rev{revision}"));
        }

        await succeededRecorder.WaitForCountAsync(2, TimeSpan.FromSeconds(10), TestToken);

        // A quiet period 3x the debounce long must not produce any extra reload.
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestToken);

        Assert.Equal(2, startedRecorder.Count);
        var handle = Assert.Single(orchestrator.Registry.Handles);
        Assert.StartsWith("v2rev5:", DescribeScript(orchestrator.Registry, handle), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReloadAsync_ReferenciaForteRetidaPeloHost_DisparaAssemblyUnloadTimedOut()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("counter.cs", ScriptSources.CounterScript("v1"));
        var options = CreateInMemoryOptions(source);
        options.Hosting.UnloadTimeout = TimeSpan.FromMilliseconds(300);
        options.Hosting.UnloadPollInterval = TimeSpan.FromMilliseconds(50);
        await using var orchestrator = new HotReloadOrchestrator(options);
        var timedOutRecorder = new EventRecorder<AssemblyUnloadTimedOutEventArgs>();
        orchestrator.AssemblyUnloadTimedOut += (_, e) => timedOutRecorder.Record(e);

        try
        {
            await orchestrator.StartAsync(TestToken);
            var handle = Assert.Single(orchestrator.Registry.Handles);
            IncrementScript(orchestrator.Registry, handle);

            PinScript(orchestrator.Registry, handle); // the intentional leak: a host static holds the instance

            source.SetScript("counter.cs", ScriptSources.CounterScript("v2"));
            var result = await orchestrator.ReloadAsync(TestToken);

            // Policy: the reload still succeeds — the leak is bounded to one generation.
            Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
            Assert.True(result.UnloadTimedOut);
            Assert.Equal("v2:1", DescribeScript(orchestrator.Registry, handle));

            var timedOutEvent = Assert.Single(timedOutRecorder.Snapshot);
            Assert.Equal(1, timedOutEvent.GenerationNumber);
            Assert.Equal(options.Hosting.UnloadTimeout, timedOutEvent.Timeout);
            Assert.True(timedOutEvent.Elapsed >= timedOutEvent.Timeout);

            // Releasing the pin lets the pending unload complete on its own.
            ReleasePin();
            var probe = orchestrator.RetiredGenerationProbes[0];
            ForceCollect(probe);
            Assert.False(probe.IsAlive);
        }
        finally
        {
            ReleasePin();
        }
    }

    [Fact]
    public async Task StartAsync_OrigemSemScripts_CarregaGeracaoVaziaEAtivaScriptDepois()
    {
        var source = new InMemoryScriptSource();
        await using var orchestrator = new HotReloadOrchestrator(CreateInMemoryOptions(source));

        await orchestrator.StartAsync(TestToken);

        Assert.Equal(1, orchestrator.CurrentGenerationNumber);
        Assert.Empty(orchestrator.Registry.Handles);

        source.SetScript("counter.cs", ScriptSources.CounterScript("v1"));
        var result = await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
        var handle = Assert.Single(orchestrator.Registry.Handles);
        Assert.Equal("v1:0", DescribeScript(orchestrator.Registry, handle));
    }

    [Fact]
    public async Task ReloadAsync_ScriptAdicionadoEDepoisRemovido_RegistroAcompanha()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("counter.cs", ScriptSources.CounterScript("v1"));
        await using var orchestrator = new HotReloadOrchestrator(CreateInMemoryOptions(source));

        await orchestrator.StartAsync(TestToken);
        Assert.Single(orchestrator.Registry.Handles);

        source.SetScript("second.cs", ScriptSources.SecondScript);
        await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(2, orchestrator.Registry.Handles.Count);
        Assert.Contains(orchestrator.Registry.Handles, h => h.TypeFullName == "SecondScript");

        source.RemoveScript("second.cs");
        await orchestrator.ReloadAsync(TestToken);

        var survivor = Assert.Single(orchestrator.Registry.Handles);
        Assert.Equal("CounterScript", survivor.TypeFullName);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PinScript(ScriptInstanceRegistry registry, ScriptHandle handle)
        => s_pinnedInstance = registry.GetAs<ICounterScript>(handle);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleasePin() => s_pinnedInstance = null;
}
