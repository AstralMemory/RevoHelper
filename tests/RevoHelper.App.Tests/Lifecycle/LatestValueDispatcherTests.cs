using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class LatestValueDispatcherTests
{
    [Fact]
    public void SlowUiGetsOnlyLatestValueAndAtMostOneQueuedCallback()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<int>();
        var updates = new LatestValueDispatcher<int>(callbacks.Enqueue, displayed.Add);
        for (var value = 0; value < 100; value++) updates.Publish(value);
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Equal(new[] { 99 }, displayed);
    }

    [Fact]
    public void HiddenWindowKeepsLatestSampleWithoutPostingAndRefreshesOnShow()
    {
        var callbacks = new Queue<Action>();
        var displayed = new List<int>();
        var updates = new LatestValueDispatcher<int>(callbacks.Enqueue, displayed.Add);
        updates.Publish(1);
        updates.SetPresentationEnabled(false);
        callbacks.Dequeue()();
        for (var value = 2; value <= 100; value++) updates.Publish(value);
        Assert.Empty(callbacks);
        Assert.Empty(displayed);
        updates.SetPresentationEnabled(true);
        Assert.Single(callbacks);
        callbacks.Dequeue()();
        Assert.Equal(new[] { 100 }, displayed);
    }
}
