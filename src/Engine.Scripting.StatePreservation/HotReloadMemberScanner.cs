using System.Reflection;
using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;

namespace Engine.Scripting.StatePreservation;

/// <summary>
/// Finds every <c>[HotReloadState]</c> field and read/write property of a type, including
/// non-public members inherited from base classes.
/// </summary>
internal static class HotReloadMemberScanner
{
    private const BindingFlags DeclaredInstanceMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static IReadOnlyList<HotReloadMember> Scan(Type type, ILogger logger)
    {
        var members = new List<HotReloadMember>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        // Walk from the most derived type up, with DeclaredOnly, so private members of base
        // classes are visible. On a duplicate key the most derived declaration wins.
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(DeclaredInstanceMembers))
            {
                if (field.GetCustomAttribute<HotReloadStateAttribute>() is not { } attribute)
                {
                    continue;
                }

                var key = attribute.Key ?? field.Name;
                if (!seenKeys.Add(key))
                {
                    Log.DuplicateStateKey(logger, key, current.FullName ?? current.Name);
                    continue;
                }

                members.Add(new HotReloadMember(field, key));
            }

            foreach (var property in current.GetProperties(DeclaredInstanceMembers))
            {
                if (property.GetCustomAttribute<HotReloadStateAttribute>() is not { } attribute)
                {
                    continue;
                }

                if (property.GetMethod is null || property.SetMethod is null || property.GetIndexParameters().Length > 0)
                {
                    Log.PropertyNotEligible(logger, property.Name, current.FullName ?? current.Name);
                    continue;
                }

                var key = attribute.Key ?? property.Name;
                if (!seenKeys.Add(key))
                {
                    Log.DuplicateStateKey(logger, key, current.FullName ?? current.Name);
                    continue;
                }

                members.Add(new HotReloadMember(property, key));
            }
        }

        return members;
    }
}
