using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Presentation;

public sealed class BoundedShutdownTests
{
    [Fact]
    public void SynchronousNativeStallDoesNotBlockCallerBeyondBudget()
    {
        using var release = new ManualResetEventSlim();
        var completed = BoundedShutdown.Run(() => { release.Wait(TimeSpan.FromSeconds(2)); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(20));
        release.Set();
        Assert.False(completed);
    }
    [Fact]
    public void CompletedAndFaultedCleanupAreDistinguished()
    {
        Assert.True(BoundedShutdown.Run(() => Task.CompletedTask, TimeSpan.FromSeconds(1)));
        Assert.False(BoundedShutdown.Run(() => throw new InvalidOperationException("test"), TimeSpan.FromSeconds(1)));
    }
}
