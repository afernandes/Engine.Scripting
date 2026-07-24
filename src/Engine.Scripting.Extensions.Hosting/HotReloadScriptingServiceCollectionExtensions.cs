using Engine.Scripting.Instances;
using Engine.Scripting.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Extensions.Hosting;

/// <summary>
/// Registers Engine.Scripting hot reload into an <see cref="IServiceCollection"/> /
/// Generic Host application.
/// </summary>
public static class HotReloadScriptingServiceCollectionExtensions
{
    /// <summary>
    /// Adds hot-reload scripting to the host: a singleton <see cref="HotReloadOrchestrator"/>
    /// (wired to the host's <see cref="ILoggerFactory"/>), its
    /// <see cref="ScriptInstanceRegistry"/> for direct injection, and a hosted service that
    /// performs the initial load on startup and watches for changes for the application
    /// lifetime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unless <see cref="HotReloadOptions.InstanceFactory"/> is set explicitly, scripts are
    /// created through <see cref="ActivatorUtilities"/>, so <b>script constructors receive
    /// dependency injection from the container</b> — a business-rule script can take a
    /// repository or any registered service in its constructor, re-resolved on every reload.
    /// Scripts are long-lived objects resolved from the root provider: inject singletons (or an
    /// <see cref="IServiceScopeFactory"/> to create scopes per operation), not scoped services.
    /// </para>
    /// <para>
    /// This package deliberately does not reference the Roslyn compiler: in source mode, assign
    /// <see cref="HotReloadOptions.Compiler"/> (e.g. <c>new IncrementalScriptCompiler(...)</c>
    /// from <c>Engine.Scripting.Compilation</c>); in precompiled mode, assign
    /// <see cref="HotReloadOptions.ImageSource"/> and no compiler is loaded at all.
    /// </para>
    /// <para>
    /// Subsequent calls are no-ops (the first registration wins).
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the <see cref="HotReloadOptions"/>.</param>
    public static IServiceCollection AddHotReloadScripting(
        this IServiceCollection services,
        Action<HotReloadOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return AddHotReloadScripting(services, (_, options) => configure(options));
    }

    /// <summary>
    /// Same as <see cref="AddHotReloadScripting(IServiceCollection, Action{HotReloadOptions})"/>,
    /// with access to the <see cref="IServiceProvider"/> during configuration — useful to pull
    /// an <c>HttpClient</c>, connection strings or other registered services into the options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Configures the <see cref="HotReloadOptions"/> using the container.</param>
    public static IServiceCollection AddHotReloadScripting(
        this IServiceCollection services,
        Action<IServiceProvider, HotReloadOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.TryAddSingleton(serviceProvider =>
        {
            var options = new HotReloadOptions();
            configure(serviceProvider, options);

            // The DI-aware default: script constructors are satisfied from the container.
            options.InstanceFactory ??= type => ActivatorUtilities.CreateInstance(serviceProvider, type);

            return new HotReloadOrchestrator(options, serviceProvider.GetService<ILoggerFactory>());
        });

        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<HotReloadOrchestrator>().Registry);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, HotReloadScriptingHostedService>());

        return services;
    }
}
