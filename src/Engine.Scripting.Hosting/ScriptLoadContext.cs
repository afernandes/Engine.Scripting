using System.Reflection;
using System.Runtime.Loader;

namespace Engine.Scripting.Hosting;

/// <summary>
/// The collectible load context that hosts exactly one script assembly generation.
/// </summary>
/// <remarks>
/// <see cref="Load"/> returns <see langword="null"/> for every dependency, which makes the BCL,
/// the abstractions package and all host assemblies resolve in the default load context. That
/// fallback is what guarantees type identity between host and script: the script's
/// <c>IReloadableScript</c> is the host's <c>IReloadableScript</c>. Only the script assembly
/// itself lives here — loaded from a stream, never from a path, so no file is ever locked.
/// </remarks>
internal sealed class ScriptLoadContext : AssemblyLoadContext
{
    public ScriptLoadContext(int generationNumber)
        : base(name: $"Engine.Scripting.Gen{generationNumber}", isCollectible: true)
    {
    }

    protected override Assembly? Load(AssemblyName assemblyName) => null;
}
