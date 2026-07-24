using System.Runtime.CompilerServices;
using Engine.Scripting.Abstractions;

namespace Engine.Scripting.Hosting.Tests;

/// <summary>
/// Helpers that confine every strong reference to a script generation inside non-inlinable
/// frames, so test methods only ever observe <see cref="WeakReference"/> probes and primitive
/// results. This is the discipline that makes unload assertions deterministic even in Debug
/// builds, where the JIT reports locals as live until the end of the method.
/// </summary>
internal static class UnloadTestHarness
{
    /// <summary>Loads a generation, touches it, unloads, and returns only the result + probe.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<(UnloadResult Result, WeakReference Probe)> LoadTouchAndUnloadAsync(
        ScriptAssemblyImage image,
        ScriptHostOptions options,
        CancellationToken cancellationToken)
    {
        var context = new ReloadableScriptContext(options);
        LoadAndTouch(context, image);
        var result = await context.UnloadCurrentGenerationAsync(cancellationToken);
        return (result, context.RetiredGenerationProbes[^1]);
    }

    /// <summary>
    /// Loads a generation, hands a script instance to <paramref name="pin"/> (typically storing
    /// it in a static field to simulate a leak), unloads, and returns the result + probe.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<(UnloadResult Result, WeakReference Probe)> LoadPinAndUnloadAsync(
        ScriptAssemblyImage image,
        ScriptHostOptions options,
        string typeName,
        Action<object> pin,
        CancellationToken cancellationToken)
    {
        var context = new ReloadableScriptContext(options);
        pin(LoadAndCreateInstance(context, image, typeName));
        var result = await context.UnloadCurrentGenerationAsync(cancellationToken);
        return (result, context.RetiredGenerationProbes[^1]);
    }

    /// <summary>Runs one load + unload cycle on a shared context, returning only the outcome.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task<UnloadOutcome> RunSingleCycleAsync(
        ReloadableScriptContext context,
        ScriptAssemblyImage image,
        CancellationToken cancellationToken)
    {
        LoadAndTouch(context, image);
        var result = await context.UnloadCurrentGenerationAsync(cancellationToken);
        return result.Outcome;
    }

    /// <summary>Forces GC cycles until <paramref name="probe"/> dies or attempts run out.</summary>
    public static void ForceCollect(WeakReference probe)
    {
        for (var i = 0; i < 10 && probe.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadAndTouch(ReloadableScriptContext context, ScriptAssemblyImage image)
    {
        var generation = context.LoadGeneration(image);
        _ = generation.Assembly.GetTypes();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object LoadAndCreateInstance(ReloadableScriptContext context, ScriptAssemblyImage image, string typeName)
    {
        var generation = context.LoadGeneration(image);
        var type = generation.Assembly.GetType(typeName, throwOnError: true)!;
        return Activator.CreateInstance(type)!;
    }
}
