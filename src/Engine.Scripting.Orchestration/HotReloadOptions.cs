using Engine.Scripting.Abstractions;
using Engine.Scripting.Hosting;

namespace Engine.Scripting.Orchestration;

/// <summary>
/// Configuration of the <see cref="HotReloadOrchestrator"/>.
/// </summary>
/// <remarks>
/// Exactly one code-acquisition mode must be configured:
/// <list type="bullet">
/// <item><description>
/// <b>Source mode</b> (development): <see cref="Source"/> — or <see cref="ScriptsPath"/> for the
/// built-in file-system source — plus a <see cref="Compiler"/> (e.g.
/// <c>new IncrementalScriptCompiler(...)</c> from <c>Engine.Scripting.Compilation</c>).
/// </description></item>
/// <item><description>
/// <b>Precompiled mode</b> (production/devices): <see cref="ImageSource"/> only. No compiler is
/// involved, so Roslyn never ships with the deployment.
/// </description></item>
/// </list>
/// </remarks>
public sealed class HotReloadOptions
{
    /// <summary>
    /// Custom script-text source (database, remote service…). Ownership stays with the caller:
    /// the orchestrator never disposes an injected source. Mutually exclusive with
    /// <see cref="ScriptsPath"/> and <see cref="ImageSource"/>.
    /// </summary>
    public IScriptSource? Source { get; set; }

    /// <summary>
    /// Directory for the built-in file-system source. Mutually exclusive with
    /// <see cref="Source"/> and <see cref="ImageSource"/>.
    /// </summary>
    public string? ScriptsPath { get; set; }

    /// <summary>File pattern for <see cref="ScriptsPath"/>; defaults to <c>*.cs</c>.</summary>
    public string SearchPattern { get; set; } = "*.cs";

    /// <summary>Whether <see cref="ScriptsPath"/> includes subdirectories (default).</summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>
    /// The compiler used in source mode. Required when <see cref="Source"/> or
    /// <see cref="ScriptsPath"/> is set.
    /// </summary>
    public IScriptCompiler? Compiler { get; set; }

    /// <summary>
    /// Precompiled-image source for production-style deployments. Ownership stays with the
    /// caller when injected. Mutually exclusive with the source-mode properties.
    /// </summary>
    public IScriptAssemblyImageSource? ImageSource { get; set; }

    /// <summary>
    /// Quiet period required after the last change notification before a reload fires;
    /// bursts of notifications collapse into one reload. Default: 250 ms.
    /// </summary>
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Whether <see cref="HotReloadOrchestrator.StartAsync"/> subscribes to the source and
    /// reloads automatically on changes. Disable for fully manual control via
    /// <see cref="HotReloadOrchestrator.ReloadAsync"/> (deterministic tests, controlled
    /// production rollouts).
    /// </summary>
    public bool EnableSourceWatching { get; set; } = true;

    /// <summary>Cooperative-unload verification options.</summary>
    public ScriptHostOptions Hosting { get; } = new();

    /// <summary>
    /// When <see langword="true"/> (default), every concrete <see cref="IReloadableScript"/>
    /// type found in a new generation that has no registered handle yet is instantiated and
    /// registered automatically.
    /// </summary>
    public bool ActivateAllReloadableScripts { get; set; } = true;

    /// <summary>
    /// Factory used to create script instances; defaults to
    /// <see cref="Activator.CreateInstance(Type)"/>. Plug a DI container here if scripts take
    /// dependencies.
    /// </summary>
    public Func<Type, object>? InstanceFactory { get; set; }
}
