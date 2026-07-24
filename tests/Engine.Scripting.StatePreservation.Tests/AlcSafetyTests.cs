using System.Runtime.CompilerServices;
using Engine.Scripting.Compilation;
using Engine.Scripting.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Engine.Scripting.StatePreservation.Tests;

/// <summary>
/// Proves the capture-time ALC policy against a real collectible generation: values of types
/// declared inside the script assembly are discarded (they could pin the retiring context and
/// could never be restored onto the next generation's brand-new types), while host/BCL-typed
/// values migrate.
/// </summary>
[CollectionDefinition("UnloadSensitive", DisableParallelization = true)]
public sealed class UnloadSensitiveCollection;

[Collection("UnloadSensitive")]
public class AlcSafetyTests
{
    private const string ScriptWithScriptTypedState = """
        public class ScriptPayload
        {
            public int Inner = 10;
        }

        public class StatefulScript
        {
            [Engine.Scripting.Abstractions.HotReloadState]
            private int _safeCounter = 77;

            [Engine.Scripting.Abstractions.HotReloadState]
            private ScriptPayload _unsafePayload = new();

            [Engine.Scripting.Abstractions.HotReloadState]
            private System.Collections.Generic.List<ScriptPayload> _unsafeList = new();

            [Engine.Scripting.Abstractions.HotReloadState]
            private System.Collections.Generic.List<int> _safeList = new() { 1, 2, 3 };
        }
        """;

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Capture_ValoresDeTiposDoScript_DescartaComWarningEMigraOsSeguros()
    {
        var image = await CompileScriptAsync(TestToken);
        var logger = new FakeLogger<StatePreservationService>();
        var service = new StatePreservationService(logger);
        await using var context = new ReloadableScriptContext(new ScriptHostOptions
        {
            UnloadTimeout = TimeSpan.FromSeconds(2),
            UnloadPollInterval = TimeSpan.FromMilliseconds(25),
        });

        var snapshot = CaptureFromLoadedScript(context, image, service);
        var unloadResult = await context.UnloadCurrentGenerationAsync(TestToken);

        // The snapshot lives on across the unload — and must not pin the retired generation.
        Assert.Equal(UnloadOutcome.Collected, unloadResult.Outcome);

        Assert.Equal(77, snapshot.Values["_safeCounter"]);
        Assert.Equal(new List<int> { 1, 2, 3 }, snapshot.Values["_safeList"]);
        Assert.Contains("_unsafePayload", snapshot.DiscardedMembers);
        Assert.Contains("_unsafeList", snapshot.DiscardedMembers);
        Assert.DoesNotContain("_unsafePayload", snapshot.Values.Keys);
        Assert.DoesNotContain("_unsafeList", snapshot.Values.Keys);
        Assert.Equal(2, logger.Collector.GetSnapshot().Count(r => r.Level == LogLevel.Warning));
    }

    private static async Task<Abstractions.ScriptAssemblyImage> CompileScriptAsync(CancellationToken cancellationToken)
    {
        var options = new ScriptCompilerOptions();
        options.AddReference(typeof(Abstractions.HotReloadStateAttribute));
        var compiler = new IncrementalScriptCompiler(options);
        compiler.AddOrUpdateSource("stateful.cs", ScriptWithScriptTypedState);

        var result = await compiler.CompileAsync(cancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Image!;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ScriptStateSnapshot CaptureFromLoadedScript(
        ReloadableScriptContext context,
        Abstractions.ScriptAssemblyImage image,
        StatePreservationService service)
    {
        var generation = context.LoadGeneration(image);
        var type = generation.Assembly.GetType("StatefulScript", throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        return service.Capture(instance);
    }
}
