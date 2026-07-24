namespace Engine.Scripting.Orchestration.Tests;

/// <summary>
/// Thread-safe event collector with awaitable count thresholds — the event-oriented alternative
/// to sleeping in tests.
/// </summary>
internal sealed class EventRecorder<TArgs>
{
    private readonly Lock _gate = new();
    private readonly List<TArgs> _records = [];
    private TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _records.Count;
            }
        }
    }

    public IReadOnlyList<TArgs> Snapshot
    {
        get
        {
            lock (_gate)
            {
                return [.. _records];
            }
        }
    }

    public void Record(TArgs args)
    {
        TaskCompletionSource completed;
        lock (_gate)
        {
            _records.Add(args);
            completed = _signal;
            _signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        completed.TrySetResult();
    }

    /// <summary>Waits until at least <paramref name="count"/> events were recorded.</summary>
    public async Task WaitForCountAsync(int count, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        while (true)
        {
            Task waitTask;
            lock (_gate)
            {
                if (_records.Count >= count)
                {
                    return;
                }

                waitTask = _signal.Task;
            }

            try
            {
                await waitTask.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"Expected at least {count} event(s) of {typeof(TArgs).Name} within {timeout}, but observed {Count}.");
            }
        }
    }
}
