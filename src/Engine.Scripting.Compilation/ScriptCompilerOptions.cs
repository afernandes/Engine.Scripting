using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Engine.Scripting.Compilation;

/// <summary>
/// Configuration of the <see cref="IncrementalScriptCompiler"/>: which assemblies scripts may
/// reference, language settings, and debug-information behavior.
/// </summary>
/// <remarks>
/// The reference set is materialized once, on the first compilation; changes made to the lists
/// afterwards are not picked up. Configure everything before the first compile.
/// </remarks>
public sealed class ScriptCompilerOptions
{
    /// <summary>
    /// Prefix of the emitted assembly name. Each compilation emits a unique name
    /// (<c>{prefix}.g{n}</c>) so stack traces and diagnostics identify the generation.
    /// </summary>
    public string AssemblyNamePrefix { get; set; } = "Engine.Scripting.Generated";

    /// <summary>
    /// When <see langword="true"/> (default), references every assembly from the host's
    /// <c>TRUSTED_PLATFORM_ASSEMBLIES</c> list, giving scripts access to the same BCL and
    /// application assemblies the host resolved at startup.
    /// </summary>
    public bool IncludeTrustedPlatformAssemblies { get; set; } = true;

    /// <summary>
    /// Extra reference assembly paths supplied by the consumer. Duplicates of assemblies already
    /// provided (by simple name) are ignored, with consumer entries taking precedence over the
    /// trusted-platform list.
    /// </summary>
    public IList<string> ReferencePaths { get; } = [];

    /// <summary>
    /// Extra references supplied as raw PE images — the escape hatch for single-file or trimmed
    /// hosts where assemblies have no <see cref="Assembly.Location"/>.
    /// </summary>
    public IList<byte[]> ReferenceImages { get; } = [];

    /// <summary>C# language version used to parse the scripts.</summary>
    public LanguageVersion LanguageVersion { get; set; } = LanguageVersion.Latest;

    /// <summary>
    /// Optimization level of the emitted assembly. <see cref="OptimizationLevel.Debug"/>
    /// (default) keeps locals inspectable under a debugger — the right choice for a hot-reload
    /// development loop.
    /// </summary>
    public OptimizationLevel OptimizationLevel { get; set; } = OptimizationLevel.Debug;

    /// <summary>Whether scripts may contain <c>unsafe</c> code.</summary>
    public bool AllowUnsafe { get; set; }

    /// <summary>Preprocessor symbols defined for every script document.</summary>
    public IList<string> PreprocessorSymbols { get; } = [];

    /// <summary>
    /// When <see langword="true"/> (default), embeds each document's source text into the
    /// portable PDB. Debuggers (Visual Studio, VS Code, Rider) can then show sources and bind
    /// breakpoints even when the script did not come from a local file — for example when the
    /// script source is a database.
    /// </summary>
    public bool EmbedSourcesInPdb { get; set; } = true;

    /// <summary>
    /// Adds a reference to a loaded assembly by its file location — typically the host assembly
    /// that defines the contracts scripts implement.
    /// </summary>
    /// <param name="assembly">Assembly whose location is added to <see cref="ReferencePaths"/>.</param>
    /// <exception cref="ScriptingConfigurationException">
    /// The assembly has no file location (single-file publish or dynamic assembly); use
    /// <see cref="ReferencePaths"/> or <see cref="ReferenceImages"/> instead.
    /// </exception>
    public void AddReference(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrEmpty(assembly.Location))
        {
            throw new ScriptingConfigurationException(
                $"Assembly '{assembly.GetName().Name}' has no file location (single-file publish or dynamic assembly). " +
                "Provide the reference explicitly via ReferencePaths (a path to a reference assembly on disk) " +
                "or ReferenceImages (the raw assembly bytes).");
        }

        ReferencePaths.Add(assembly.Location);
    }

    /// <summary>Adds a reference to the assembly that declares <paramref name="typeInAssembly"/>.</summary>
    /// <param name="typeInAssembly">Any type from the assembly to reference.</param>
    public void AddReference(Type typeInAssembly)
    {
        ArgumentNullException.ThrowIfNull(typeInAssembly);
        AddReference(typeInAssembly.Assembly);
    }
}
