using System.Reflection;

namespace Engine.Scripting.Hosting;

/// <summary>
/// One loaded generation of the script assembly, living inside its own collectible
/// <c>AssemblyLoadContext</c>.
/// </summary>
/// <remarks>
/// <b>Do not store long-lived references</b> to this object, to <see cref="Assembly"/>, or to
/// anything reached through it (types, instances, delegates): any such reference kept across a
/// reload prevents the cooperative unload from ever completing. Resolve what you need, use it,
/// and let it go — stable identity across reloads belongs to script handles, not to types.
/// </remarks>
public sealed class ScriptGeneration
{
    internal ScriptGeneration(int number, string assemblyName, Assembly assembly)
    {
        Number = number;
        AssemblyName = assemblyName;
        Assembly = assembly;
    }

    /// <summary>1-based, monotonically increasing generation number within the context.</summary>
    public int Number { get; }

    /// <summary>Simple name of the loaded assembly (unique per generation).</summary>
    public string AssemblyName { get; }

    /// <summary>The loaded script assembly.</summary>
    public Assembly Assembly { get; }
}
