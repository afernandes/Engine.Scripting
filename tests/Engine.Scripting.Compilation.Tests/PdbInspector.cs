using System.Reflection.Metadata;

namespace Engine.Scripting.Compilation.Tests;

/// <summary>
/// Reads a portable PDB and checks for embedded-source blobs — the objective proof that
/// debuggers can bind breakpoints to script documents.
/// </summary>
internal static class PdbInspector
{
    // Well-known custom debug information kind for embedded source in portable PDBs.
    private static readonly Guid EmbeddedSourceKind = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    public static bool HasEmbeddedSource(byte[] pdbBytes, string documentName)
    {
        using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdbBytes));
        var reader = provider.GetMetadataReader();

        foreach (var documentHandle in reader.Documents)
        {
            var document = reader.GetDocument(documentHandle);
            if (reader.GetString(document.Name) != documentName)
            {
                continue;
            }

            foreach (var cdiHandle in reader.GetCustomDebugInformation(documentHandle))
            {
                var cdi = reader.GetCustomDebugInformation(cdiHandle);
                if (reader.GetGuid(cdi.Kind) == EmbeddedSourceKind && reader.GetBlobBytes(cdi.Value).Length > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
