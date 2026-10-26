using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Presentation;

public sealed class LayeredFrequencyRecoveryTests
{
    [Fact]
    public async Task OuterFailurePreservesManualJournalForNextSuccessfulRecovery()
    {
        var dynamicPending = true;
        var manualPending = true;
        var cap = 3000;
        Task<bool> RestoreDynamic(CancellationToken _) => Task.FromResult(!dynamicPending);
        Task<bool> RestoreManual(CancellationToken _)
        { cap = 0; manualPending = false; return Task.FromResult(true); }
        Assert.False(await LayeredFrequencyRecovery.RunAsync(RestoreDynamic, RestoreManual, default));
        Assert.True(manualPending);
        Assert.Equal(3000, cap);
        dynamicPending = false;
        cap = 4000;
        Assert.True(await LayeredFrequencyRecovery.RunAsync(RestoreDynamic, RestoreManual, default));
        Assert.False(manualPending);
        Assert.Equal(0, cap);
    }

    [Fact]
    public async Task TimedOutOuterCannotLaterTriggerManualRecovery()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var manualCalls = 0;
        using var cts = new CancellationTokenSource();
        var restore = LayeredFrequencyRecovery.RunAsync(_ => completion.Task,
            _ => { manualCalls++; return Task.FromResult(true); }, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore);
        completion.SetResult(true);
        Assert.Equal(0, manualCalls);
    }

    [Fact]
    public async Task OuterExceptionDoesNotTouchManualLayer()
    {
        var called = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => LayeredFrequencyRecovery.RunAsync(
            _ => throw new InvalidOperationException("restore failed"),
            _ => { called = true; return Task.FromResult(true); }, default));
        Assert.False(called);
    }
}
