namespace Engine.Scripting.Instances;

/// <summary>Mutable registry slot behind a <see cref="ScriptHandle"/>.</summary>
internal sealed class ScriptInstanceEntry
{
    public required string TypeFullName { get; init; }

    /// <summary>The current instance, or <see langword="null"/> while detached during a reload.</summary>
    public object? Instance { get; set; }
}
