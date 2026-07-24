using System.Reflection;
using Engine.Scripting.Abstractions;
using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Orchestration;

/// <summary>
/// Discovers concrete <see cref="IReloadableScript"/> types in a generation's assembly,
/// tolerating partial type-load failures.
/// </summary>
internal static class ScriptActivator
{
    public static IReadOnlyList<Type> GetActivatableScriptTypes(Assembly assembly, ILogger logger)
    {
        Type?[] candidates;
        try
        {
            candidates = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var firstError = exception.LoaderExceptions.FirstOrDefault(e => e is not null)?.Message ?? "unknown";
            Log.PartialTypeLoad(logger, exception.LoaderExceptions.Length, firstError);
            candidates = exception.Types;
        }

        var scripts = new List<Type>();
        foreach (var type in candidates)
        {
            if (type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                && typeof(IReloadableScript).IsAssignableFrom(type))
            {
                scripts.Add(type);
            }
        }

        return scripts;
    }
}
