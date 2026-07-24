using Engine.Scripting.Orchestration;
using Microsoft.Extensions.Hosting;

namespace Engine.Scripting.Extensions.Hosting;

/// <summary>
/// Drives the <see cref="HotReloadOrchestrator"/> lifecycle inside a Generic Host: the initial
/// load happens during host startup, watching runs for the application lifetime, and shutdown
/// stops the watcher gracefully.
/// </summary>
/// <remarks>
/// The orchestrator's lifetime token is <see cref="IHostApplicationLifetime.ApplicationStopping"/>
/// (not the startup token, which only governs startup aborts). Disposal of the orchestrator
/// itself belongs to the container, which owns the singleton.
/// </remarks>
internal sealed class HotReloadScriptingHostedService : IHostedService, IDisposable
{
    private readonly HotReloadOrchestrator _orchestrator;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private CancellationTokenSource? _orchestratorLifetime;

    public HotReloadScriptingHostedService(HotReloadOrchestrator orchestrator, IHostApplicationLifetime applicationLifetime)
    {
        _orchestrator = orchestrator;
        _applicationLifetime = applicationLifetime;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _orchestratorLifetime = CancellationTokenSource.CreateLinkedTokenSource(_applicationLifetime.ApplicationStopping);

        // While starting up, an aborted host startup must also abort the initial script load;
        // once startup completes this registration is dropped and only ApplicationStopping
        // governs the orchestrator.
        using var startupAbort = cancellationToken.Register(
            static state => ((CancellationTokenSource)state!).Cancel(),
            _orchestratorLifetime);

        await _orchestrator.StartAsync(_orchestratorLifetime.Token).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => _orchestrator.StopAsync(cancellationToken);

    public void Dispose()
    {
        _orchestratorLifetime?.Dispose();
        _orchestratorLifetime = null;
    }
}
