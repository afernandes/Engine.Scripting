namespace Engine.Scripting.Abstractions;

/// <summary>
/// An in-memory script assembly: PE bytes plus optional portable PDB bytes.
/// </summary>
/// <remarks>
/// The byte arrays are treated as immutable by the whole pipeline; callers must not mutate them.
/// The image is what travels between compilation and hosting — nothing is ever loaded from a
/// file path, so no file lock is taken on any assembly.
/// </remarks>
/// <param name="AssemblyName">Simple name of the assembly (no extension).</param>
/// <param name="PeBytes">The PE image produced by the compiler.</param>
/// <param name="PdbBytes">
/// The portable PDB image, or <see langword="null"/> when debug symbols were not produced.
/// Keeping the PDB enables breakpoints and readable stack traces inside scripts.
/// </param>
public sealed record ScriptAssemblyImage(string AssemblyName, byte[] PeBytes, byte[]? PdbBytes)
{
    /// <summary>
    /// Writes the image to <paramref name="directory"/> as <c>{AssemblyName}.dll</c> (and
    /// <c>{AssemblyName}.pdb</c> when symbols are present), creating the directory if needed.
    /// This is the publish step of the "compile once, distribute the binary" flow.
    /// </summary>
    /// <param name="directory">Target directory.</param>
    /// <param name="cancellationToken">Token that aborts the write.</param>
    /// <returns>The full path of the written <c>.dll</c>.</returns>
    public async Task<string> WriteToDirectoryAsync(string directory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory.CreateDirectory(directory);

        var dllPath = Path.Combine(directory, AssemblyName + ".dll");
        await File.WriteAllBytesAsync(dllPath, PeBytes, cancellationToken).ConfigureAwait(false);

        if (PdbBytes is not null)
        {
            var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            await File.WriteAllBytesAsync(pdbPath, PdbBytes, cancellationToken).ConfigureAwait(false);
        }

        return dllPath;
    }

    /// <summary>
    /// Reads an image previously produced by a build step: the <c>.dll</c> at
    /// <paramref name="dllPath"/> plus a sibling <c>.pdb</c> when one exists.
    /// </summary>
    /// <param name="dllPath">Full path of the assembly file.</param>
    /// <param name="cancellationToken">Token that aborts the read.</param>
    public static async Task<ScriptAssemblyImage> ReadFromFileAsync(string dllPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dllPath);

        var peBytes = await File.ReadAllBytesAsync(dllPath, cancellationToken).ConfigureAwait(false);

        var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
        byte[]? pdbBytes = File.Exists(pdbPath)
            ? await File.ReadAllBytesAsync(pdbPath, cancellationToken).ConfigureAwait(false)
            : null;

        return new ScriptAssemblyImage(Path.GetFileNameWithoutExtension(dllPath), peBytes, pdbBytes);
    }
}
