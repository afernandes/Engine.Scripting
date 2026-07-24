using Engine.Scripting.Orchestration.Sources;
using static Engine.Scripting.Orchestration.Tests.OrchestratorTestHelpers;

namespace Engine.Scripting.Orchestration.Tests;

/// <summary>
/// Precompiled mode end-to-end: a "build server" compiles the scripts once, publishes
/// dll + pdb to disk, and the orchestrator hot-swaps generations from the image — no compiler
/// configured, which is exactly what a production/device deployment looks like.
/// </summary>
[Collection("UnloadSensitive")]
public class ImageSourceReloadTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ImageSource_NovaImagemPublicada_RecarregaPreservandoEstado()
    {
        using var directory = new TempScriptDirectory();
        var dllPath = directory.FilePath("scripts.dll");

        var imageV1 = await CompileImageAsync(ScriptSources.CounterScript("v1"), TestToken);
        await PublishImageAsync(imageV1, dllPath, TestToken);

        var options = new HotReloadOptions
        {
            ImageSource = new FileSystemAssemblyImageSource(dllPath),
            EnableSourceWatching = false,
        };
        ApplyFastUnload(options.Hosting);

        await using var orchestrator = new HotReloadOrchestrator(options);
        await orchestrator.StartAsync(TestToken);

        var handle = Assert.Single(orchestrator.Registry.Handles);
        IncrementScript(orchestrator.Registry, handle);
        IncrementScript(orchestrator.Registry, handle);
        Assert.Equal("v1:2", DescribeScript(orchestrator.Registry, handle));

        var imageV2 = await CompileImageAsync(ScriptSources.CounterScript("v2"), TestToken);
        await PublishImageAsync(imageV2, dllPath, TestToken);

        var result = await orchestrator.ReloadAsync(TestToken);

        Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
        Assert.False(result.UnloadTimedOut);
        Assert.Equal("v2:2", DescribeScript(orchestrator.Registry, handle));

        var probe = Assert.Single(orchestrator.RetiredGenerationProbes);
        ForceCollect(probe);
        Assert.False(probe.IsAlive);
    }

    [Fact]
    public async Task StartAsync_SemImagemPublicada_SucedeSemGeracao()
    {
        using var directory = new TempScriptDirectory();
        var options = new HotReloadOptions
        {
            ImageSource = new FileSystemAssemblyImageSource(directory.FilePath("scripts.dll")),
            EnableSourceWatching = false,
        };
        ApplyFastUnload(options.Hosting);

        await using var orchestrator = new HotReloadOrchestrator(options);
        await orchestrator.StartAsync(TestToken);

        Assert.Equal(0, orchestrator.CurrentGenerationNumber);
        Assert.Empty(orchestrator.Registry.Handles);
    }
}
