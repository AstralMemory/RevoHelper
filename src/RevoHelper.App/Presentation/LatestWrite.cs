namespace RevoHelper.App.Presentation;

/// <summary>
/// One hardware slot. It runs one write at a time, and a newer post replaces any write
/// still waiting, so after the current write only the newest choice goes out.
/// </summary>
public sealed class LatestWrite(Action<Exception>? onError = null)
{
    private readonly object sync = new();
    private Func<Task>? pending;
    private TaskCompletionSource? idle;
    private bool running;
    private int delayedPosts;
    private long newest;

    /// <summary>A delay merges rapid edits, such as dragging a slider, into one write.</summary>
    public void Post(Func<Task> write, TimeSpan delay = default)
    {
        long post;
        lock (sync)
        {
            post = ++newest;
            pending = null;
            idle ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (delay > TimeSpan.Zero) delayedPosts++;
        }
        if (delay > TimeSpan.Zero) _ = EnqueueAfterDelayAsync(write, delay, post);
        else Enqueue(write, post);
    }

    /// <summary>Completes once no write is running, waiting or delayed.</summary>
    public Task WhenIdleAsync()
    {
        lock (sync) return idle?.Task ?? Task.CompletedTask;
    }

    private async Task EnqueueAfterDelayAsync(Func<Task> write, TimeSpan delay, long post)
    {
        await Task.Delay(delay);
        lock (sync) delayedPosts--;
        Enqueue(write, post);
    }

    private void Enqueue(Func<Task> write, long post)
    {
        lock (sync)
        {
            if (post != newest)
            {
                CompleteIdleIfDone();
                return;
            }
            pending = write;
            if (running) return;
            running = true;
        }
        _ = DrainAsync();
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Func<Task>? next;
            lock (sync)
            {
                next = pending;
                pending = null;
                if (next is null)
                {
                    running = false;
                    CompleteIdleIfDone();
                    return;
                }
            }
            try
            {
                await next();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                onError?.Invoke(exception);
            }
        }
    }

    private void CompleteIdleIfDone()
    {
        if (running || pending is not null || delayedPosts > 0) return;
        idle?.TrySetResult();
        idle = null;
    }
}
