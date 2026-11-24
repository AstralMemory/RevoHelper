using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class FrequencyWriteGateTests
{
    [Fact]
    public async Task BeginShutdown_RefusesNewWritesButStillReads()
    {
        var inner = new FakeController();
        var gate = new FrequencyWriteGate(inner);

        gate.BeginShutdown();

        Assert.False((await gate.ApplyAsync(ProcessorFrequencyLimitPolicy.Uniform(3100), default)).Succeeded);
        Assert.False((await gate.PrepareForExplicitDisableAsync(default)).Succeeded);
        Assert.Equal(0, inner.Writes);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), await gate.ReadAsync(default));
    }

    [Fact]
    public async Task WaitForIdle_WaitsForAWriteAdmittedBeforeShutdown()
    {
        var release = new TaskCompletionSource();
        var inner = new FakeController { Release = release.Task };
        var gate = new FrequencyWriteGate(inner);

        var write = gate.ApplyAsync(ProcessorFrequencyLimitPolicy.Uniform(3100), default).AsTask();
        await inner.Entered;
        gate.BeginShutdown();
        var idle = gate.WaitForIdleAsync(default);
        Assert.False(idle.IsCompleted);

        release.SetResult();

        Assert.True((await write).Succeeded);
        await idle;
        Assert.Equal(1, inner.Writes);
    }

    [Fact]
    public async Task Writes_WaitForTheStartupRecoveryToFinish()
    {
        var startup = new TaskCompletionSource();
        var inner = new FakeController();
        var gate = new FrequencyWriteGate(inner, startup.Task);

        var write = gate.ApplyAsync(ProcessorFrequencyLimitPolicy.Uniform(3100), default).AsTask();
        await Task.Delay(50);
        Assert.False(write.IsCompleted);
        Assert.Equal(0, inner.Writes);

        startup.SetException(new InvalidOperationException("recovery failed"));

        // A failed attempt has still finished; the controller decides what it left behind.
        Assert.True((await write).Succeeded);
        Assert.Equal(1, inner.Writes);
    }

    [Fact]
    public async Task Reads_WaitForTheStartupRecoverySoTheyNeverSeeValuesItIsAboutToRestore()
    {
        var startup = new TaskCompletionSource();
        var inner = new FakeController();
        var gate = new FrequencyWriteGate(inner, startup.Task);

        var read = gate.ReadAsync(default).AsTask();
        await Task.Delay(50);
        Assert.False(read.IsCompleted);

        startup.SetResult();

        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), await read);
    }

    [Fact]
    public async Task Writes_AreRefusedWhileTheStartupRecoveryOverrunsTheWait()
    {
        var startup = new TaskCompletionSource();
        var inner = new FakeController();
        var gate = new FrequencyWriteGate(inner, startup.Task, TimeSpan.FromMilliseconds(50));

        Assert.False((await gate.ApplyAsync(ProcessorFrequencyLimitPolicy.Uniform(3100), default)).Succeeded);
        Assert.Equal(0, inner.Writes);
    }

    [Fact]
    public async Task Reads_FailWhileTheStartupRecoveryOverrunsTheWait()
    {
        // A value the recovery may still restore must not become a baseline or a recheck result.
        var startup = new TaskCompletionSource();
        var inner = new FakeController();
        var gate = new FrequencyWriteGate(inner, startup.Task, TimeSpan.FromMilliseconds(50));

        var failure = await Assert.ThrowsAsync<TimeoutException>(() => gate.ReadAsync(default).AsTask());
        Assert.Equal(FrequencyWriteGate.StartupRecoveryStillRunningReadMessage, failure.Message);
        Assert.Equal(0, inner.Reads);

        startup.SetResult();
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), await gate.ReadAsync(default));
    }

    private sealed class FakeController : IProcessorFrequencyLimitController
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Release { get; init; } = Task.CompletedTask;
        public Task Entered => entered.Task;
        public int Writes { get; private set; }
        public int Reads { get; private set; }

        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return ValueTask.FromResult(ProcessorFrequencyLimitPolicy.Uniform(0));
        }

        public async ValueTask<ControlResult> ApplyAsync(
            ProcessorFrequencyLimitPolicy policy,
            CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await Release;
            Writes++;
            return new ControlResult(true, "ok");
        }

        public ValueTask<ControlResult> PrepareForExplicitDisableAsync(CancellationToken cancellationToken)
        {
            Writes++;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }
}
