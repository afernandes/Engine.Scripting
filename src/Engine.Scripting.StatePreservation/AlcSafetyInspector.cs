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
/// A migratable value is also a <i>restorable</i> value: types loaded outside the script context
/// (BCL, host, abstractions) keep their identity across reloads, whereas a script-declared type
/// has a brand-new <see cref="Type"/> in the next generation and could never be assigned anyway.
/// </para>
/// <para>
/// Known limitation (documented): values inspected are the <i>runtime</i> types of the captured
/// object graph roots — a script-defined payload hidden inside an <c>object</c>-typed collection
/// element escapes this static check.
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

        return IsTypeOutsideContext(value.GetType(), scriptContext);
    }

    private static bool IsTypeOutsideContext(Type type, AssemblyLoadContext scriptContext)
    {
        if (AssemblyLoadContext.GetLoadContext(type.Assembly) == scriptContext)
        {
            return false;
        }

        if (type.IsArray)
        {
            return type.GetElementType() is not { } elementType || IsTypeOutsideContext(elementType, scriptContext);
        }

        if (type.IsConstructedGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (!IsTypeOutsideContext(argument, scriptContext))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
