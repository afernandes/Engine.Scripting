using Microsoft.Extensions.Logging;

namespace Engine.Scripting.Orchestration;

/// <summary>
/// Source-agnostic trailing debounce: every <see cref="Signal"/> restarts the quiet-period
/// window (by cancelling and re-creating a linked <see cref="CancellationTokenSource"/>), and
/// only a window that survives the full interval invokes the callback. N rapid notifications —
/// file-watcher bursts or database NOTIFY storms alike — therefore collapse into exactly one
/// pipeline run. No thread is ever blocked: the wait is a cancellable <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
/// </summary>
internal sealed class ChangeDebouncer : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _onQuietPeriod;
    private readonly CancellationToken _lifetimeToken;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private CancellationTokenSource? _windowCts;
    private bool _disposed;

    public ChangeDebouncer(TimeSpan interval, Func<CancellationToken, Task> onQuietPeriod, CancellationToken lifetimeToken, ILogger logger)
    {
        _interval = interval;
        _onQuietPeriod = onQuietPeriod;
        _lifetimeToken = lifetimeToken;
        _logger = logger;
    }

    /// <summary>Notifies that a change arrived, restarting the quiet-period window.</summary>
    public void Signal()
    {
        CancellationToken windowToken;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _windowCts?.Cancel();
            _windowCts?.Dispose();
            _windowCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
            windowToken = _windowCts.Token;
        }

        _ = RunWindowAsync(windowToken);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _windowCts?.Cancel();
            _windowCts?.Dispose();
            _windowCts = null;
        }
    }

    private async Task RunWindowAsync(CancellationToken windowToken)
    {
        try
        {
            await Task.Delay(_interval, windowToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer notification restarted the window (or the host is shutting down);
            // this window dies silently.
            return;
        }

        try
        {
            // The callback runs under the host lifetime token, NOT the window token: a change
            // arriving while the pipeline runs must schedule the next run, never abort this one.
            await _onQuietPeriod(_lifetimeToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
        catch (Exception exception)
        {
            Log.DebounceCallbackFailed(_logger, exception);
        }
    }
}
