using System.Reflection;

namespace Engine.Scripting.StatePreservation;

/// <summary>
/// A field or read/write property marked with <c>[HotReloadState]</c>, normalized behind a
/// single get/set surface.
/// </summary>
internal sealed class HotReloadMember
{
    private readonly FieldInfo? _field;
    private readonly PropertyInfo? _property;

    public HotReloadMember(FieldInfo field, string key)
    {
        _field = field;
        Key = key;
    }

    public HotReloadMember(PropertyInfo property, string key)
    {
        _property = property;
        Key = key;
    }

    /// <summary>Snapshot key: the attribute's explicit key or the member name.</summary>
    public string Key { get; }

    /// <summary>Declared type of the member.</summary>
    public Type MemberType => _field?.FieldType ?? _property!.PropertyType;

    /// <summary>Member name (for logging).</summary>
    public string Name => _field?.Name ?? _property!.Name;

    public object? GetValue(object instance)
        => _field is not null ? _field.GetValue(instance) : _property!.GetValue(instance);

    public void SetValue(object instance, object? value)
    {
        if (_field is not null)
        {
            _field.SetValue(instance, value);
        }
        else
        {
            _property!.SetValue(instance, value);
        }
    }
}
