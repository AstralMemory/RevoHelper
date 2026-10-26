namespace RevoHelper.App.Lifecycle;

/// <summary>One discovery at a time, bounded retries per request, and no work after shutdown.</summary>
public sealed class RetryableDiscoveryCoordinator : IDisposable
{
    private readonly object sync = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly CancellationToken cancellationToken;
    private readonly Func<CancellationToken, Task<bool>> discover;
    private readonly Func<bool, Task> busyChanged;
    private readonly Action<Exception> reportFailure;
    private readonly Func<CancellationToken, Task> retryDelay;
    private readonly int maxAttempts;
    private Task? runningTask;
    private bool stopped;

    public RetryableDiscoveryCoordinator(
        Func<CancellationToken, Task<bool>> discover,
        Func<bool, Task> busyChanged,
        Action<Exception> reportFailure,
        int maxAttempts = 3,
        Func<CancellationToken, Task>? retryDelay = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        this.discover = discover;
        this.busyChanged = busyChanged;
        this.reportFailure = reportFailure;
        this.maxAttempts = maxAttempts;
        this.retryDelay = retryDelay ?? (token => Task.Delay(TimeSpan.FromSeconds(1), token));
        cancellationToken = lifetime.Token;
    }

    public Task RequestAsync()
    {
        lock (sync)
        {
            if (stopped) return Task.CompletedTask;
            if (runningTask is { IsCompleted: false }) return runningTask;
            return runningTask = Task.Run(RunAsync);
        }
    }

    private async Task RunAsync()
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await busyChanged(true).ConfigureAwait(false);
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await discover(cancellationToken).ConfigureAwait(false)) return;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    reportFailure(exception);
                }

                if (attempt + 1 < maxAttempts)
                    await retryDelay(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { reportFailure(exception); }
        finally
        {
            try { await busyChanged(false).ConfigureAwait(false); }
            catch (Exception exception) { reportFailure(exception); }
        }
    }

    public void Dispose()
    {
        Task? pending;
        lock (sync)
        {
            if (stopped) return;
            stopped = true;
            pending = runningTask;
        }
        lifetime.Cancel();
        if (pending is null || pending.IsCompleted) lifetime.Dispose();
        else _ = pending.ContinueWith(_ => lifetime.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
