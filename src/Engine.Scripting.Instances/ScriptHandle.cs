namespace Engine.Scripting.Instances;

/// <summary>
/// Stable logical identity of a script across hot-reload generations.
/// </summary>
/// <remarks>
/// The handle is the only thing a consumer should hold on to between reloads: it is a plain
/// value (a <see cref="Guid"/> plus a type name) and can never pin a script generation in
/// memory. Resolve the current instance on every use through the registry — storing the
/// resolved instance in a host field is exactly the leak that makes a cooperative unload time
/// out.
/// </remarks>
/// <param name="Id">Unique identity, stable across reloads.</param>
/// <param name="TypeFullName">
/// Full name of the script type this handle is bound to; used to re-create the instance from
/// each new generation.
/// </param>
public readonly record struct ScriptHandle(Guid Id, string TypeFullName);
