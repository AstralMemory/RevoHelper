using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class RetryableDiscoveryCoordinatorTests
{
    [Fact]
    public async Task TransientFailures_RetryWithinBound_AndFinishBusyState()
    {
        var attempts = 0;
        var busy = new List<bool>();
        using var coordinator = new RetryableDiscoveryCoordinator(
            _ => Task.FromResult(++attempts == 3),
            value => { busy.Add(value); return Task.CompletedTask; },
            exception => Assert.Fail(exception.Message), retryDelay: _ => Task.CompletedTask);

        await coordinator.RequestAsync();

        Assert.Equal(3, attempts);
        Assert.Equal(new[] { true, false }, busy);
    }

    [Fact]
    public async Task ExhaustedRequest_AllowsLaterReconnectOrManualRequest()
    {
        var attempts = 0;
        var available = false;
        using var coordinator = new RetryableDiscoveryCoordinator(
            _ => { attempts++; return Task.FromResult(available); },
            _ => Task.CompletedTask, exception => Assert.Fail(exception.Message),
            retryDelay: _ => Task.CompletedTask);

        await coordinator.RequestAsync();
        Assert.Equal(3, attempts);
        available = true;
        await coordinator.RequestAsync();
        Assert.Equal(4, attempts);
    }

    [Fact]
    public async Task ConcurrentRequests_ShareTheSameInFlightDiscovery()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var coordinator = new RetryableDiscoveryCoordinator(
            _ => { attempts++; started.TrySetResult(); return release.Task; },
            _ => Task.CompletedTask, exception => Assert.Fail(exception.Message));

        var first = coordinator.RequestAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(first, coordinator.RequestAsync());
        release.SetResult(true);
        await first;
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Shutdown_CancelsRetries_AndRejectsNewRequests()
    {
        var delayStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        using var coordinator = new RetryableDiscoveryCoordinator(
            _ => { attempts++; return Task.FromResult(false); },
            _ => Task.CompletedTask, exception => Assert.Fail(exception.Message),
            retryDelay: token => { delayStarted.TrySetResult(); return Task.Delay(Timeout.InfiniteTimeSpan, token); });

        var pending = coordinator.RequestAsync();
        await delayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Dispose();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.RequestAsync();
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ReadException_DoesNotPermanentlyLockTheCoordinator()
    {
        var failures = new List<Exception>();
        var attempts = 0;
        using var coordinator = new RetryableDiscoveryCoordinator(
            _ => ++attempts == 1 ? throw new InvalidOperationException("Temporary failure") : Task.FromResult(true),
            _ => Task.CompletedTask, failures.Add, retryDelay: _ => Task.CompletedTask);

        await coordinator.RequestAsync();
        Assert.Equal(2, attempts);
        Assert.Single(failures);
    }
}
