using Engine.Scripting.Orchestration.Sources;
using static Engine.Scripting.Orchestration.Tests.OrchestratorTestHelpers;

namespace Engine.Scripting.Orchestration.Tests;

[Collection("UnloadSensitive")]
public sealed class OrchestratorDisposalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAsync_ComInstanciaViva_LimpaRegistroExecutaHookEColeta(bool hookThrows)
    {
        await using var source = new InMemoryScriptSource();
        var options = CreateInMemoryOptions(source);
        var calls = 0;
        Action cleanup = () =>
        {
            calls++;
            if (hookThrows) throw new InvalidOperationException("cleanup failure");
        };
        options.InstanceFactory = type => Activator.CreateInstance(type, cleanup)!;
        source.SetScript("cleanup.cs", """
            public class CleanupScript : Engine.Scripting.Abstractions.IReloadableScript
            {
                private readonly System.Action _cleanup;
                public CleanupScript(System.Action cleanup) => _cleanup = cleanup;
                public System.Threading.Tasks.ValueTask OnAfterReloadAsync(System.Threading.CancellationToken ct)
                    => System.Threading.Tasks.ValueTask.CompletedTask;
                public System.Threading.Tasks.ValueTask OnBeforeReloadAsync(System.Threading.CancellationToken ct)
                {
                    _cleanup();
                    return System.Threading.Tasks.ValueTask.CompletedTask;
                }
            }
            """);
        var orchestrator = new HotReloadOrchestrator(options);
        await orchestrator.StartAsync(TestContext.Current.CancellationToken);
        Assert.Single(orchestrator.Registry.Handles);
        await orchestrator.DisposeAsync();
        await orchestrator.DisposeAsync();
        Assert.Equal(1, calls);
        Assert.Empty(orchestrator.Registry.GetLiveEntries());
        Assert.False(Assert.Single(orchestrator.RetiredGenerationProbes).IsAlive);
        GC.KeepAlive(orchestrator);
    }
}
