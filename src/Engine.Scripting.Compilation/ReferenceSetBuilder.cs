using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace Engine.Scripting.Compilation;

/// <summary>
/// Combines consumer-supplied references with the trusted-platform set, deduplicating by
/// assembly simple name with consumer entries winning.
/// </summary>
internal static class ReferenceSetBuilder
{
    public static ImmutableArray<MetadataReference> Build(ScriptCompilerOptions options)
    {
        var seenSimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();

        foreach (var path in options.ReferencePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (seenSimpleNames.Add(Path.GetFileNameWithoutExtension(fullPath)))
            {
                references.Add(MetadataReference.CreateFromFile(fullPath));
            }
        }

        foreach (var image in options.ReferenceImages)
        {
            // No simple name is known for a raw image without parsing it; images are the
            // consumer's explicit choice, so they are always included as-is.
            references.Add(MetadataReference.CreateFromImage(ImmutableCollectionsMarshal.AsImmutableArray(image)));
        }

        if (options.IncludeTrustedPlatformAssemblies)
        {
            foreach (var reference in TrustedPlatformAssemblyLocator.GetReferences())
            {
                var simpleName = Path.GetFileNameWithoutExtension(reference.FilePath ?? string.Empty);
                if (seenSimpleNames.Add(simpleName))
                {
                    references.Add(reference);
                }
            }
        }

        return references.ToImmutable();
    }
}
