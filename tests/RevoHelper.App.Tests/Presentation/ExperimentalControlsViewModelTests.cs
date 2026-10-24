using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Tests.Presentation;

public sealed class ExperimentalControlsViewModelTests
{
    [Fact]
    public async Task UnchangedTelemetryDoesNotRefreshUnrelatedOrUnchangedUiProperties()
    {
        var backend = new FakeDynamic();
        using var vm = new ExperimentalControlsViewModel(backend, null);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        await vm.SetDynamicEnabledAsync(true);
        Assert.Contains(nameof(vm.IsDynamicEnabled), notifications);
        Assert.DoesNotContain(null, notifications);
        Assert.DoesNotContain(nameof(vm.BootWaterMessage), notifications);
        notifications.Clear();
        for (var i = 0; i < 50; i++) await vm.ObserveAsync(new(DateTimeOffset.UtcNow, []));
        Assert.Equal(50, backend.Observations);
        Assert.Empty(notifications);
        await vm.PauseAsync("restore", CancellationToken.None);
        Assert.Contains(nameof(vm.IsDynamicEnabled), notifications);
        Assert.Contains(nameof(vm.DynamicMessage), notifications);
    }

    [Fact]
    public async Task BusyDispatcherCoalescesUiUpdatesButStillProcessesEveryControlSample()
    {
        var backend = new FakeDynamic();
        var queue = new Queue<Action>();
        using var vm = new ExperimentalControlsViewModel(backend, null, queue.Enqueue);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        await vm.SetDynamicEnabledAsync(true);
        for (var i = 0; i < 50; i++) await vm.ObserveAsync(new(DateTimeOffset.UtcNow, []));
        Assert.Equal(50, backend.Observations);
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.Contains(nameof(vm.IsDynamicEnabled), notifications);
        Assert.DoesNotContain(nameof(vm.BootWaterMessage), notifications);
        await vm.PauseAsync("restore", CancellationToken.None);
        Assert.Single(queue);
        queue.Dequeue()();
        Assert.False(vm.IsDynamicEnabled);
        Assert.Equal("restore", vm.DynamicMessage);
    }

    [Fact]
    public async Task AutomaticPauseDoesNotRetryAfterRecoveryFailure()
    {
        var backend = new FakeDynamic { FailRestore = true };
        using var vm = new ExperimentalControlsViewModel(backend, null);
        await vm.SetDynamicEnabledAsync(true);
        await vm.PauseAutomaticallyAsync("telemetry error");
        await vm.PauseAutomaticallyAsync("telemetry still unavailable");
        Assert.Equal(2, backend.Calls);
        Assert.True(vm.HasRecoveryPending);
    }

    [Fact]
    public async Task TelemetryWatchdogUsesElapsedTimeAndPausesWithoutNewSamples()
    {
        var clock = new ManualClock(); var backend = new FakeDynamic();
        using var vm = new ExperimentalControlsViewModel(backend, null, timeProvider: clock);
        await vm.SetDynamicEnabledAsync(true);
        clock.Advance(TimeSpan.FromSeconds(3)); Assert.True(vm.IsDynamicEnabled);
        clock.Advance(TimeSpan.FromSeconds(2));
        await backend.Paused.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(vm.IsDynamicEnabled);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long timestamp;
        private Action? tick;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public void Advance(TimeSpan time) { timestamp += time.Ticks; tick?.Invoke(); }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { tick = () => callback(state); return new ManualTimer(() => tick = null); }
        private sealed class ManualTimer(Action dispose) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() => dispose();
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    [Fact]
    public async Task ExitDisablesFutureObservationsAndRestores()
    {
        var backend = new FakeDynamic(); using var vm = new ExperimentalControlsViewModel(backend, null);
        await vm.SetDynamicEnabledAsync(true);
        await vm.ShutdownAsync(CancellationToken.None);
        await vm.ObserveAsync(new(DateTimeOffset.UtcNow, []));
        Assert.False(vm.IsDynamicEnabled); Assert.False(vm.CanToggleDynamic);
        Assert.Equal(2, backend.Calls); Assert.Equal(0, backend.Observations);
    }

    [Fact]
    public async Task NativeRestoreIsStartedOffTheCallingThread()
    {
        var backend = new FakeDynamic { RestoreEntered = new(TaskCreationOptions.RunContinuationsAsynchronously), RestoreRelease = new(false) };
        using var vm = new ExperimentalControlsViewModel(backend, null);
        var restoration = vm.ShutdownAsync(CancellationToken.None);
        try
        {
            await backend.RestoreEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(restoration.IsCompleted);
        }
        finally { backend.RestoreRelease.Set(); await restoration; backend.RestoreRelease.Dispose(); }
    }

    [Fact]
    public async Task BusyObservationIsNotDroppedWhilePreviousObservationIsInFlight()
    {
        var backend = new FakeDynamic { ObservationHold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var vm = new ExperimentalControlsViewModel(backend, null);
        await vm.SetDynamicEnabledAsync(true);
        var first = vm.ObserveAsync(new(DateTimeOffset.UtcNow, []));
        await backend.FirstObservation.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = vm.ObserveAsync(new(DateTimeOffset.UtcNow, []));
        try { Assert.Equal(2, backend.Observations); }
        finally { backend.ObservationHold.SetResult(); await Task.WhenAll(first, second); }
    }

    [Fact]
    public void ConstructionDoesNotEnableOrReadNativeState()
    {
        var dynamic = new FakeDynamic(); var water = new FakeWater();
        using var vm = new ExperimentalControlsViewModel(dynamic, water);
        Assert.False(vm.IsDynamicEnabled); Assert.Equal(0, dynamic.Calls); Assert.Equal(0, water.Calls);
    }

    [Fact]
    public async Task ToggleAndRecoveryUseBackendStatus()
    {
        var backend = new FakeDynamic(); using var vm = new ExperimentalControlsViewModel(backend, null);
        await vm.SetDynamicEnabledAsync(true);
        Assert.True(vm.IsDynamicEnabled);
        await vm.RecoverAsync(); Assert.False(vm.IsDynamicEnabled); Assert.Equal(2, backend.Calls);
    }

    [Fact]
    public async Task ManualWriteIsRejectedWhenRecoveryRemainsPending()
    {
        var backend = new FakeDynamic { FailRestore = true };
        using var vm = new ExperimentalControlsViewModel(backend, null);
        Assert.False(await vm.PauseForManualControlAsync());
        Assert.True(vm.HasRecoveryPending);
    }

    [Fact]
    public async Task PreparingForManualControlRestoresOwnedLimitWithoutDisablingFeature()
    {
        var backend = new FakeDynamic();
        using var vm = new ExperimentalControlsViewModel(backend, null);
        await vm.SetDynamicEnabledAsync(true);

        Assert.True(await vm.PauseForManualControlAsync());

        Assert.True(vm.IsDynamicEnabled);
        Assert.Equal(1, backend.Preparations);
    }

    [Fact]
    public async Task WaterOperationsReflectFailuresAndNeverInventSuccess()
    {
        var water = new FakeWater(); using var vm = new ExperimentalControlsViewModel(null, water);
        await vm.InstallBootWaterAsync();
        Assert.Equal("模拟失败", vm.BootWaterMessage); Assert.True(vm.CanManageBootWater);
        await vm.RefreshBootWaterAsync(); Assert.Equal("尚未安装", vm.BootWaterMessage);
        await vm.RemoveBootWaterAsync(); Assert.Equal(3, water.Calls);
    }

    private sealed class FakeDynamic : IDynamicPowerController
    {
        public int Calls; public bool FailRestore;
        public int Preparations;
        public int Observations;
        public TaskCompletionSource? ObservationHold;
        public TaskCompletionSource FirstObservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? RestoreEntered;
        public ManualResetEventSlim? RestoreRelease;
        public DynamicPowerStatus Status { get; private set; } = new(false, "关闭", 0, false, false);
        public Task SetEnabledAsync(bool enabled, CancellationToken token) { Calls++; Status = Status with { Enabled = enabled }; return Task.CompletedTask; }
        public Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token)
        { Interlocked.Increment(ref Observations); FirstObservation.TrySetResult(); return ObservationHold?.Task ?? Task.CompletedTask; }
        public Task RecoverAsync(CancellationToken token) => PauseAsync("恢复", token);
        public Task PrepareForExternalChangeAsync(string reason, CancellationToken token)
        {
            Preparations++;
            Calls++;
            Status = new(Status.Enabled, reason, 0, false, FailRestore);
            return Task.CompletedTask;
        }
        public Task PauseAsync(string reason, CancellationToken token)
        { RestoreEntered?.TrySetResult(); RestoreRelease?.Wait(TimeSpan.FromSeconds(2)); Calls++; Status = new(false, reason, 0, FailRestore, FailRestore); Paused.TrySetResult(); return Task.CompletedTask; }
    }
    private sealed class FakeWater : IBootWaterTaskManager
    {
        public int Calls;
        public Task<BootWaterTaskStatus> ReadAsync(CancellationToken token) { Calls++; return Task.FromResult(new BootWaterTaskStatus(BootWaterTaskState.NotInstalled, "尚未安装")); }
        public Task<BootWaterTaskStatus> InstallAsync(CancellationToken token) { Calls++; return Task.FromResult(new BootWaterTaskStatus(BootWaterTaskState.Error, "模拟失败")); }
        public Task<BootWaterTaskStatus> RemoveAsync(CancellationToken token) { Calls++; return Task.FromResult(new BootWaterTaskStatus(BootWaterTaskState.NotInstalled, "已移除")); }
    }
}
