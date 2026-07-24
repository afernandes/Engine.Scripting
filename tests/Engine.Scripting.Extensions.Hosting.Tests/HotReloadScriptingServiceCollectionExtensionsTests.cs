using System.Runtime.CompilerServices;
using Engine.Scripting.Abstractions;
using Engine.Scripting.Compilation;
using Engine.Scripting.Instances;
using Engine.Scripting.Orchestration;
using Engine.Scripting.Orchestration.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Engine.Scripting.Extensions.Hosting.Tests;

[Collection("UnloadSensitive")]
public class HotReloadScriptingServiceCollectionExtensionsTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public void AddHotReloadScripting_Registra_OrchestratorRegistryEHostedServiceUmaUnicaVez()
    {
        var services = new ServiceCollection();

        services.AddHotReloadScripting(options => ConfigureInMemory(options, new InMemoryScriptSource()));
        services.AddHotReloadScripting(options => ConfigureInMemory(options, new InMemoryScriptSource()));

        Assert.Single(services, d => d.ServiceType == typeof(HotReloadOrchestrator));
        Assert.Single(services, d => d.ServiceType == typeof(ScriptInstanceRegistry));
        Assert.Single(services, d => d.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void AddHotReloadScripting_RegistryResolvido_EOMesmoDoOrchestrator()
    {
        var services = new ServiceCollection();
        services.AddHotReloadScripting(options => ConfigureInMemory(options, new InMemoryScriptSource()));

        using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<HotReloadOrchestrator>();
        var registry = provider.GetRequiredService<ScriptInstanceRegistry>();

        Assert.Same(orchestrator.Registry, registry);
    }

    [Fact]
    public async Task InstanceFactory_CustomizadoNoConfigure_NaoESobrescritoPeloDefault()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("plain.cs", PlainScript);
        var factoryCalls = 0;

        var services = new ServiceCollection();
        services.AddHotReloadScripting(options =>
        {
            ConfigureInMemory(options, source);
            options.InstanceFactory = type =>
            {
                Interlocked.Increment(ref factoryCalls);
                return Activator.CreateInstance(type)!;
            };
        });

        await using var provider = services.BuildServiceProvider();
        var orchestrator = provider.GetRequiredService<HotReloadOrchestrator>();
        await orchestrator.StartAsync(TestToken);

        Assert.Equal(1, factoryCalls);
        Assert.Single(orchestrator.Registry.Handles);
    }

    [Fact]
    public async Task Host_ScriptComDependenciaNoConstrutor_RecebeServicoDoContainerEPreservaEstadoNoReload()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("greeter.cs", DiGreeterScript("v1"));

        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IGreetingProvider, TestGreetingProvider>();
                services.AddHotReloadScripting(options => ConfigureInMemory(options, source));
            })
            .Build();

        await host.StartAsync(TestToken);
        try
        {
            var orchestrator = host.Services.GetRequiredService<HotReloadOrchestrator>();
            var registry = host.Services.GetRequiredService<ScriptInstanceRegistry>();
            var handle = Assert.Single(registry.Handles);

            // Constructor injection worked and [HotReloadState] counts the calls.
            Assert.Equal("v1:from-container:1", GreetScript(registry, handle));
            Assert.Equal("v1:from-container:2", GreetScript(registry, handle));

            source.SetScript("greeter.cs", DiGreeterScript("v2"));
            var result = await orchestrator.ReloadAsync(TestToken);

            Assert.Equal(ReloadOutcome.Succeeded, result.Outcome);
            Assert.False(result.UnloadTimedOut);

            // New generation: dependency re-injected by the container, counter preserved.
            Assert.Equal("v2:from-container:3", GreetScript(registry, handle));

            // The decisive check: instances created through ActivatorUtilities must not keep
            // the retired generation alive (e.g. via a static constructor-info cache).
            var probe = Assert.Single(orchestrator.RetiredGenerationProbes);
            ForceCollect(probe);
            Assert.False(probe.IsAlive, "a DI-activated script pinned the retired generation");
        }
        finally
        {
            await host.StopAsync(TestToken);
        }
    }

    [Fact]
    public async Task Host_StartEStop_CicloCompletoSemErros()
    {
        var source = new InMemoryScriptSource();
        source.SetScript("plain.cs", PlainScript);

        var host = new HostBuilder()
            .ConfigureServices(services => services.AddHotReloadScripting(options => ConfigureInMemory(options, source)))
            .Build();

        await host.StartAsync(TestToken);
        var orchestrator = host.Services.GetRequiredService<HotReloadOrchestrator>();
        Assert.Equal(1, orchestrator.CurrentGenerationNumber);

        await host.StopAsync(TestToken);
        host.Dispose(); // sync dispose path: container disposes the orchestrator via the IDisposable bridge

        await Assert.ThrowsAsync<ObjectDisposedException>(() => orchestrator.ReloadAsync(TestToken));
    }

    private static void ConfigureInMemory(HotReloadOptions options, InMemoryScriptSource source)
    {
        var compilerOptions = new ScriptCompilerOptions();
        compilerOptions.AddReference(typeof(IReloadableScript));
        compilerOptions.AddReference(typeof(IGreeterScript));

        options.Source = source;
        options.Compiler = new IncrementalScriptCompiler(compilerOptions);
        options.EnableSourceWatching = false;
        options.Hosting.UnloadTimeout = TimeSpan.FromSeconds(2);
        options.Hosting.UnloadPollInterval = TimeSpan.FromMilliseconds(25);
    }

    private static string DiGreeterScript(string versionTag) => $$"""
        using System.Threading;
        using System.Threading.Tasks;
        using Engine.Scripting.Abstractions;
        using Engine.Scripting.Extensions.Hosting.Tests;

        public class DiGreeterScript : IReloadableScript, IGreeterScript
        {
            private readonly IGreetingProvider _provider;

            [HotReloadState]
            private int _calls;

            public DiGreeterScript(IGreetingProvider provider) => _provider = provider;

            public string Greet() => "{{versionTag}}:" + _provider.GetGreeting() + ":" + ++_calls;

            public ValueTask OnBeforeReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask OnAfterReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }
        """;

    private const string PlainScript = """
        using System.Threading;
        using System.Threading.Tasks;
        using Engine.Scripting.Abstractions;

        public class PlainScript : IReloadableScript
        {
            public ValueTask OnBeforeReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask OnAfterReloadAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        }
        """;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GreetScript(ScriptInstanceRegistry registry, ScriptHandle handle)
    {
        var script = registry.GetAs<IGreeterScript>(handle);
        Assert.NotNull(script);
        return script.Greet();
    }

    private static void ForceCollect(WeakReference probe)
    {
        for (var i = 0; i < 10 && probe.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
