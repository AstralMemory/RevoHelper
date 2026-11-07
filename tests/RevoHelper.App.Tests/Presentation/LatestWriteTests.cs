using RevoHelper.App.Presentation;

namespace RevoHelper.App.Tests.Presentation;

public sealed class LatestWriteTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task WaitingWrites_KeepOnlyTheNewestAfterTheCurrentWrite()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = new List<string>();
        var write = new LatestWrite();

        write.Post(async () => { ran.Add("first"); await release.Task; });
        write.Post(() => { ran.Add("replaced"); return Task.CompletedTask; });
        write.Post(() => { ran.Add("newest"); return Task.CompletedTask; });
        Assert.Equal(["first"], ran);

        release.SetResult();
        await write.WhenIdleAsync().WaitAsync(Limit);

        Assert.Equal(["first", "newest"], ran);
    }

    [Fact]
    public async Task Writes_NeverOverlap()
    {
        var running = 0;
        var peak = 0;
        var write = new LatestWrite();
        async Task Work()
        {
            peak = Math.Max(peak, Interlocked.Increment(ref running));
            await Task.Delay(10);
            Interlocked.Decrement(ref running);
        }

        for (var i = 0; i < 5; i++)
        {
            write.Post(Work);
            await Task.Delay(3);
        }
        await write.WhenIdleAsync().WaitAsync(Limit);

        Assert.Equal(1, peak);
    }

    [Fact]
    public async Task DelayedPosts_MergeIntoTheNewestOne()
    {
        var ran = new List<int>();
        var write = new LatestWrite();

        for (var value = 1; value <= 4; value++)
        {
            var captured = value;
            write.Post(() => { ran.Add(captured); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(40));
        }
        await write.WhenIdleAsync().WaitAsync(Limit);

        Assert.Equal([4], ran);
    }

    [Fact]
    public async Task DelayedPost_IsReplacedByAnImmediateOne()
    {
        var ran = new List<string>();
        var write = new LatestWrite();

        write.Post(() => { ran.Add("delayed"); return Task.CompletedTask; }, TimeSpan.FromMilliseconds(30));
        write.Post(() => { ran.Add("immediate"); return Task.CompletedTask; });
        await write.WhenIdleAsync().WaitAsync(Limit);
        await Task.Delay(60);

        Assert.Equal(["immediate"], ran);
    }

    [Fact]
    public async Task FailedWrite_IsReportedAndDoesNotStopTheNextOne()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        var ran = false;
        var write = new LatestWrite(exception => errors.Add(exception.Message));

        write.Post(async () => { await release.Task; throw new InvalidOperationException("simulated"); });
        write.Post(() => { ran = true; return Task.CompletedTask; });
        release.SetResult();
        await write.WhenIdleAsync().WaitAsync(Limit);

        Assert.Equal(["simulated"], errors);
        Assert.True(ran);
    }

    [Fact]
    public async Task IdleSlot_CompletesImmediately()
    {
        await new LatestWrite().WhenIdleAsync().WaitAsync(Limit);
    }
}
