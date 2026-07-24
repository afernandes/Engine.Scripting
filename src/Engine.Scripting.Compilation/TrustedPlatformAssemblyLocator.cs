using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Engine.Scripting.Compilation;

/// <summary>
/// Resolves and caches metadata references for every assembly in the host's
/// <c>TRUSTED_PLATFORM_ASSEMBLIES</c> list.
/// </summary>
/// <remarks>
/// Creating hundreds of <see cref="PortableExecutableReference"/> instances costs noticeable
/// time and memory, so the set is materialized once per process and shared by every compiler
/// instance.
/// </remarks>
internal static class TrustedPlatformAssemblyLocator
{
    private static readonly Lazy<ImmutableArray<PortableExecutableReference>> CachedReferences =
        new(Create, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The process-wide trusted-platform reference set (possibly empty).</summary>
    public static ImmutableArray<PortableExecutableReference> GetReferences() => CachedReferences.Value;

    private static ImmutableArray<PortableExecutableReference> Create()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string trustedAssemblies || trustedAssemblies.Length == 0)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<PortableExecutableReference>();
        foreach (var path in trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(path))
            {
                builder.Add(MetadataReference.CreateFromFile(path));
            }
        }

        return builder.ToImmutable();
    }
}
