using System.Runtime.Loader;

namespace Engine.Scripting.StatePreservation;

/// <summary>
/// Decides whether a captured value may migrate across generations.
/// </summary>
/// <remarks>
/// <para>
/// The rule is applied at <b>capture time</b>, and that placement is load-bearing: if the
/// snapshot dictionary held even one value whose type lives in the retiring collectible
/// <see cref="AssemblyLoadContext"/>, the snapshot itself (alive in the reload pipeline's async
/// state machine) would be a GC root pinning the old context — every unload would time out.
/// Filtering here makes the snapshot structurally incapable of pinning the old generation.
/// </para>
/// <para>
/// Only known scalar data types, arrays and exact List/Nullable constructions over safe data
/// types are accepted. Arbitrary host objects can transitively retain a collectible context.
/// </para>
/// <para>
/// Opaque containers, delegates, reflection objects and custom host classes are rejected.
/// No arbitrary getters or enumerable implementations are invoked by this policy.
/// </para>
/// </remarks>
internal static class AlcSafetyInspector
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="value"/> can safely migrate out of
    /// <paramref name="scriptContext"/>.
    /// </summary>
    public static bool CanMigrate(object? value, AssemblyLoadContext? scriptContext)
    {
        if (value is null)
        {
            return true;
        }

        // When the instance does not live in a collectible context (plain host objects, unit
        // tests), there is no generation to pin and everything may migrate.
        if (scriptContext is null || !scriptContext.IsCollectible)
        {
            return true;
        }

        return IsSafeDataType(value.GetType());
    }

    private static bool IsSafeDataType(Type type)
    {
        if (type.Assembly.IsCollectible)
        {
            return false;
        }

        if (type.IsArray)
        {
            return IsSafeDataType(type.GetElementType()!);
        }

        if (type.IsConstructedGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            if (definition != typeof(List<>) && definition != typeof(Nullable<>))
            {
                return false;
            }
            foreach (var argument in type.GetGenericArguments())
            {
                if (!IsSafeDataType(argument))
                {
                    return false;
                }
            }
            return true;
        }

        return type.IsPrimitive || type.IsEnum || type == typeof(string)
            || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(DateOnly) || type == typeof(TimeOnly);
    }
}
