namespace Engine.Scripting.Abstractions;

/// <summary>
/// Marks a field or property whose value must survive a hot reload.
/// </summary>
/// <remarks>
/// <para>
/// Before the current script generation is unloaded, the values of all members carrying this
/// attribute are captured into a snapshot. After the new generation is loaded and a fresh
/// instance is created, compatible values are written back onto the new instance.
/// </para>
/// <para>
/// Only values whose runtime type lives <b>outside</b> the collectible script load context can
/// migrate between generations (primitives, <see cref="string"/>, host/BCL types). Values of
/// types declared inside the script assembly itself are discarded with a warning, because the
/// reloaded assembly produces brand-new <see cref="Type"/> identities. Values transported inside
/// <see cref="object"/>-typed containers cannot be inspected statically and are the consumer's
/// responsibility.
/// </para>
/// <para>
/// Properties must expose both a getter and a setter to participate. Static members are not
/// supported: statics are reset on every reload, exactly like Unity's domain reload.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class HotReloadStateAttribute : Attribute
{
    /// <summary>
    /// Marks the member using its own name as the snapshot key.
    /// </summary>
    public HotReloadStateAttribute()
    {
    }

    /// <summary>
    /// Marks the member using an explicit snapshot key, allowing the member to be renamed in a
    /// later script version without losing its preserved value.
    /// </summary>
    /// <param name="key">Stable key used to match the value across generations.</param>
    public HotReloadStateAttribute(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    /// <summary>
    /// Explicit snapshot key, or <see langword="null"/> to use the member name.
    /// </summary>
    public string? Key { get; }
}
