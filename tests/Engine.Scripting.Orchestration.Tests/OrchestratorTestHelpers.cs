using System.Runtime.CompilerServices;
using Engine.Scripting.Abstractions;
using Engine.Scripting.Compilation;
using Engine.Scripting.Hosting;
using Engine.Scripting.Instances;
using Engine.Scripting.Orchestration.Sources;

namespace Engine.Scripting.Orchestration.Tests;

/// <summary>
/// Shared factory + non-inlinable script interactions. Interactions with script instances are
/// confined to <c>NoInlining</c> static helpers so test methods never hold a strong reference to
/// a generation across a reload (that would be the exact leak the library diagnoses).
/// </summary>
internal static class OrchestratorTestHelpers
{
    public static ScriptCompilerOptions CreateCompilerOptions()
    {
        var compilerOptions = new ScriptCompilerOptions();
        compilerOptions.AddReference(typeof(IReloadableScript));
        compilerOptions.AddReference(typeof(ICounterScript));
        return compilerOptions;
    }

    public static HotReloadOptions CreateInMemoryOptions(
        InMemoryScriptSource source,
        bool enableWatching = false,
        TimeSpan? debounceInterval = null)
    {
        var options = new HotReloadOptions
        {
            Source = source,
            Compiler = new IncrementalScriptCompiler(CreateCompilerOptions()),
            EnableSourceWatching = enableWatching,
            DebounceInterval = debounceInterval ?? TimeSpan.FromMilliseconds(150),
        };
        ApplyFastUnload(options.Hosting);
        return options;
    }

    public static void ApplyFastUnload(ScriptHostOptions hosting)
    {
        hosting.UnloadTimeout = TimeSpan.FromSeconds(2);
        hosting.UnloadPollInterval = TimeSpan.FromMilliseconds(25);
    }

    public static async Task<ScriptAssemblyImage> CompileImageAsync(string source, CancellationToken cancellationToken)
    {
        var compiler = new IncrementalScriptCompiler(CreateCompilerOptions());
        compiler.AddOrUpdateSource("image-script.cs", source);
        var result = await compiler.CompileAsync(cancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.Image!;
    }

    /// <summary>Publishes an image as {dllPath} + sibling .pdb, like a build/deploy step would.</summary>
    public static async Task PublishImageAsync(ScriptAssemblyImage image, string dllPath, CancellationToken cancellationToken)
    {
        await File.WriteAllBytesAsync(dllPath, image.PeBytes, cancellationToken);
        if (image.PdbBytes is not null)
        {
            await File.WriteAllBytesAsync(Path.ChangeExtension(dllPath, ".pdb"), image.PdbBytes, cancellationToken);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int IncrementScript(ScriptInstanceRegistry registry, ScriptHandle handle)
    {
        var script = registry.GetAs<ICounterScript>(handle);
        Assert.NotNull(script);
        return script.Increment();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string DescribeScript(ScriptInstanceRegistry registry, ScriptHandle handle)
    {
        var script = registry.GetAs<ICounterScript>(handle);
        Assert.NotNull(script);
        return script.Describe();
    }

    public static void ForceCollect(WeakReference probe)
    {
        for (var i = 0; i < 10 && probe.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }
}
