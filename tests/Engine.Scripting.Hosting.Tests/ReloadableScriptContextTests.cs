using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Engine.Scripting.Abstractions;

namespace Engine.Scripting.Hosting.Tests;

[Collection("UnloadSensitive")]
public class ReloadableScriptContextTests
{
    private const string ScriptSource = "public class HostedScript { public int Value { get; set; } = 41; }";

    private static object? s_pinnedInstance;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static ScriptHostOptions FastOptions => new()
    {
        UnloadTimeout = TimeSpan.FromSeconds(2),
        UnloadPollInterval = TimeSpan.FromMilliseconds(25),
    };

    [Fact]
    public async Task LoadGeneration_ImagemValida_CarregaEmAlcColetavelComNomeDaGeracao()
    {
        var image = await TestImages.CompileAsync(ScriptSource, TestToken);
        await using var context = new ReloadableScriptContext(FastOptions);

        var (generationNumber, assemblyName, isCollectible, contextName) = LoadAndInspect(context, image);

        Assert.Equal(1, generationNumber);
        Assert.Equal(image.AssemblyName, assemblyName);
        Assert.True(isCollectible);
        Assert.Equal("Engine.Scripting.Gen1", contextName);
        Assert.NotNull(context.CurrentGeneration);
    }

    [Fact]
    public async Task LoadGeneration_ComGeracaoViva_LancaInvalidOperationException()
    {
        var image = await TestImages.CompileAsync(ScriptSource, TestToken);
        await using var context = new ReloadableScriptContext(FastOptions);

        _ = context.LoadGeneration(image);

        Assert.Throws<InvalidOperationException>(() => context.LoadGeneration(image));
    }

    [Fact]
    public async Task UnloadCurrentGenerationAsync_SemGeracao_RetornaNoGeneration()
    {
        await using var context = new ReloadableScriptContext(FastOptions);

        var result = await context.UnloadCurrentGenerationAsync(TestToken);

        Assert.Equal(UnloadOutcome.NoGeneration, result.Outcome);
        Assert.Equal(0, result.GcCycles);
    }

    [Fact]
    public async Task UnloadCurrentGenerationAsync_SemReferenciasExternas_ColetaEProbeMorre()
    {
        var image = await TestImages.CompileAsync(ScriptSource, TestToken);

        var (result, probe) = await UnloadTestHarness.LoadTouchAndUnloadAsync(image, FastOptions, TestToken);

        Assert.Equal(UnloadOutcome.Collected, result.Outcome);
        Assert.True(result.GcCycles >= 1);
        UnloadTestHarness.ForceCollect(probe);
        Assert.False(probe.IsAlive);
    }

    [Fact]
    public async Task UnloadCurrentGenerationAsync_ReferenciaForteRetida_RetornaTimedOut()
    {
        var image = await TestImages.CompileAsync(ScriptSource, TestToken);
        var options = new ScriptHostOptions
        {
            UnloadTimeout = TimeSpan.FromMilliseconds(300),
            UnloadPollInterval = TimeSpan.FromMilliseconds(50),
        };

        try
        {
            var (result, probe) = await UnloadTestHarness.LoadPinAndUnloadAsync(
                image, options, "HostedScript", instance => s_pinnedInstance = instance, TestToken);

            Assert.Equal(UnloadOutcome.TimedOut, result.Outcome);
            Assert.True(probe.IsAlive, "the pinned instance should keep the load context alive");

            ReleasePin();
            UnloadTestHarness.ForceCollect(probe);
            Assert.False(probe.IsAlive, "releasing the pin should let the pending unload complete");
        }
        finally
        {
            ReleasePin();
        }
    }

    [Fact]
    public async Task RunSingleCycleAsync_DezCiclosConsecutivos_NaoAcumulaAlcsVivos()
    {
        var image = await TestImages.CompileAsync(ScriptSource, TestToken);
        await using var context = new ReloadableScriptContext(FastOptions);

        for (var cycle = 0; cycle < 10; cycle++)
        {
            var outcome = await UnloadTestHarness.RunSingleCycleAsync(context, image, TestToken);
            Assert.Equal(UnloadOutcome.Collected, outcome);
        }

        var probes = context.RetiredGenerationProbes;
        Assert.Equal(10, probes.Count);
        foreach (var probe in probes)
        {
            UnloadTestHarness.ForceCollect(probe);
            Assert.False(probe.IsAlive);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int GenerationNumber, string AssemblyName, bool IsCollectible, string? ContextName) LoadAndInspect(
        ReloadableScriptContext context,
        ScriptAssemblyImage image)
    {
        var generation = context.LoadGeneration(image);
        var loadContext = AssemblyLoadContext.GetLoadContext(generation.Assembly);
        return (generation.Number, generation.AssemblyName, loadContext?.IsCollectible ?? false, loadContext?.Name);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReleasePin() => s_pinnedInstance = null;
}
