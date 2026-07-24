using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Hosting;

/// <summary>Source-generated log messages for the hosting feature (event ids 20xx).</summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Loaded script generation {GenerationNumber} ({AssemblyName}) into a collectible AssemblyLoadContext")]
    public static partial void GenerationLoaded(ILogger logger, int generationNumber, string assemblyName);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug,
        Message = "Cooperative unload of generation {GenerationNumber} initiated")]
    public static partial void UnloadInitiated(ILogger logger, int generationNumber);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Information,
        Message = "Generation {GenerationNumber} collected after {GcCycles} GC cycle(s) in {ElapsedMilliseconds} ms")]
    public static partial void UnloadCollected(ILogger logger, int generationNumber, int gcCycles, long elapsedMilliseconds);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning,
        Message = "Generation {GenerationNumber} was NOT collected within {TimeoutMilliseconds} ms ({GcCycles} GC cycle(s)). "
            + "A strong reference still pins the collectible AssemblyLoadContext — usual suspects: a static field holding a script "
            + "type/instance, an event subscription or delegate/closure that was not removed in OnBeforeReloadAsync, a serializer/"
            + "TypeDescriptor cache keyed by a script Type, a thread still executing script code, or an attached debugger. "
            + "The unload stays pending and completes when the reference dies.")]
    public static partial void UnloadTimedOut(ILogger logger, int generationNumber, long timeoutMilliseconds, int gcCycles);
}
