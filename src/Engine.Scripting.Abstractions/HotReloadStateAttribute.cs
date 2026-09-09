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
/// Collectible scripts preserve known scalar data types and arrays/List/Nullable containers
/// composed of those types. Custom objects, opaque containers, delegates and reflection objects
/// are discarded with a warning because they can retain the retiring generation.
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
