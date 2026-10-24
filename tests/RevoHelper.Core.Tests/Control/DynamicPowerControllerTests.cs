using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Tests.Control;

public sealed class DynamicPowerControllerTests
{
    [Fact]
    public async Task RollingTenMinuteWindowCapsWhenAtLeastNinetyPercentWasIdle()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);

        // 600 one-second intervals: 60 busy seconds followed by 540 idle seconds.
        await f.Tick(cpu: 70);
        for (var second = 0; second < 60; second++) await f.Tick(cpu: 70);
        for (var second = 0; second < 540; second++) await f.Tick();

        Assert.True(f.Controller.Status.IsLimited);
        Assert.True(f.Controller.Status.Enabled);
        Assert.Equal(600, f.Controller.Status.IdleSeconds);
        Assert.Equal(90, f.Controller.Status.IdlePercent);
    }

    [Fact]
    public async Task BriefBusyPeriodDoesNotDiscardTheRollingObservationWindow()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);

        await f.Tick();
        await f.Idle(300);
        await f.Tick(cpu: 70);

        Assert.InRange(f.Controller.Status.IdleSeconds, 300, 301);
        Assert.True(f.Controller.Status.Enabled);
        Assert.False(f.Controller.Status.IsLimited);
    }

    [Fact]
    public async Task PreparingForPerformanceChangeRestoresLeaseButKeepsFeatureEnabled()
    {
        var f = await Fixture.LimitedAfterRollingWindow();

        await f.Controller.PrepareForExternalChangeAsync("切换性能档位后重新观察。", default);

        Assert.True(f.Controller.Status.Enabled);
        Assert.False(f.Controller.Status.IsLimited);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
        Assert.Equal(f.Original, f.Backend.Values);
        Assert.Null(f.Store.Lease);
    }

    [Fact]
    public async Task CapsOnlyAfterRollingTenMinuteWindowAndRestoresImmediatelyOnBusy()
    {
        var f = new Fixture();
        Assert.False(f.Controller.Status.Enabled);
        await f.Controller.ObserveAsync(f.Sample(), default);
        Assert.Equal(0, f.Backend.Reads);
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(600);
        Assert.Empty(f.Backend.Writes);
        await f.Tick();
        Assert.True(f.Controller.Status.IsLimited);
        Assert.Equal(new DynamicFrequencyValues(3000, 2200, 3000), f.Backend.Values);
        Assert.NotNull(f.Store.Lease);
        Assert.Equal(2, f.Backend.Writes.Count);
        await f.Tick(cpu: 30);
        Assert.False(f.Controller.Status.IsLimited);
        Assert.True(f.Controller.Status.Enabled);
        Assert.Equal(f.Original, f.Backend.Values);
        Assert.Null(f.Store.Lease);
    }

    [Theory]
    [InlineData(null, 0d, true)]
    [InlineData(double.NaN, 0d, true)]
    [InlineData(101d, 0d, true)]
    [InlineData(-1d, 0d, true)]
    [InlineData(40d, 0d, false)]
    [InlineData(1d, null, true)]
    [InlineData(1d, double.NaN, true)]
    [InlineData(1d, 10d, false)]
    public async Task InvalidResetsWindowWhileBusyIsRecorded(double? core, double? gpu, bool resetsWindow)
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(300);
        Assert.Equal(299, f.Controller.Status.IdleSeconds);
        await f.Tick(core: core, gpu: gpu);
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(resetsWindow ? 0 : 300, f.Controller.Status.IdleSeconds);
    }

    [Theory]
    [InlineData(-10)]
    [InlineData(1)]
    public async Task StaleOrFutureSampleExitsCap(int offset)
    {
        var f = await Fixture.Limited();
        await f.Controller.ObserveAsync(f.Sample() with { CapturedAt = f.Clock.Now.AddSeconds(offset) }, default);
        Assert.False(f.Controller.Status.IsLimited);
        Assert.Equal(f.Original, f.Backend.Values);
    }

    [Fact]
    public async Task GapAndBackwardsTimeResetIdle()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(300);
        f.Clock.Now = f.Clock.Now.AddSeconds(4);
        await f.Controller.ObserveAsync(f.Sample(), default);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
        Assert.Empty(f.Backend.Writes);
        await f.Tick();
        await f.Controller.ObserveAsync(f.Sample() with { CapturedAt = f.Clock.Now.AddSeconds(-1) }, default);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
    }

    [Fact]
    public async Task ExternalEditIsPreservedAndDisablesAutomaticMode()
    {
        var f = await Fixture.Limited();
        f.Backend.Values = f.Backend.Values with { Class0Mhz = 4500 };
        await f.Tick();
        Assert.False(f.Controller.Status.Enabled);
        Assert.Equal(new DynamicFrequencyValues(4500, 2200, 5200), f.Backend.Values);
        Assert.Null(f.Store.Lease);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PowerContextChangeRestoresOriginalContextWithoutImmediateNewCap(bool scheme)
    {
        var f = await Fixture.Limited();
        var originalContext = f.Backend.Context;
        f.Backend.Context = scheme ? new(Guid.NewGuid(), true) : originalContext with { IsAc = false };
        await f.Tick();
        Assert.False(f.Controller.Status.IsLimited);
        Assert.True(f.Controller.Status.Enabled);
        Assert.Equal(originalContext, f.Backend.LastWriteContext);
        Assert.Equal(f.Original, f.Backend.Values);
    }

    [Fact]
    public async Task PersistFailureNeverWritesAndPauses()
    {
        var f = new Fixture();
        f.Store.FailSave = true;
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(601);
        Assert.Empty(f.Backend.Writes);
        Assert.False(f.Controller.Status.Enabled);
    }

    [Fact]
    public async Task PartialWriteFailureRetainsJournalAndExplicitRecoveryRestoresOnlyOwnedFields()
    {
        var f = new Fixture();
        f.Backend.FailWriteNumber = 2;
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(601);
        Assert.False(f.Controller.Status.Enabled);
        Assert.True(f.Controller.Status.HasRecoveryPending);
        Assert.NotNull(f.Store.Lease);
        var writes = f.Backend.Writes.Count;
        await f.Tick();
        Assert.Equal(writes, f.Backend.Writes.Count);
        f.Backend.FailWriteNumber = null;
        var restarted = new DynamicPowerController(f.Backend, f.Store, f.Clock);
        await restarted.RecoverAsync(default);
        Assert.Equal(f.Original, f.Backend.Values);
        Assert.False(restarted.Status.Enabled);
        Assert.False(restarted.Status.HasRecoveryPending);
        Assert.Null(f.Store.Lease);
    }

    [Fact]
    public async Task DisableAndPauseRestoreAndDoNotObserveHardwareAgain()
    {
        var f = await Fixture.Limited();
        await f.Controller.PauseAsync("manual change", default);
        Assert.False(f.Controller.Status.Enabled);
        Assert.Equal(f.Original, f.Backend.Values);
        var reads = f.Backend.Reads;
        await f.Tick();
        Assert.Equal(reads, f.Backend.Reads);
    }

    [Fact]
    public async Task RecoveryFailureRetainsJournalAndDoesNotRetryEverySecond()
    {
        var f = await Fixture.Limited();
        f.Backend.FailWriteNumber = f.Backend.Writes.Count + 1;
        await f.Controller.SetEnabledAsync(false, default);
        Assert.True(f.Controller.Status.HasRecoveryPending);
        var writes = f.Backend.Writes.Count;
        await f.Tick();
        Assert.Equal(writes, f.Backend.Writes.Count);
    }

    [Fact]
    public async Task CanceledCommandDoesNotWrite()
    {
        var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Controller.SetEnabledAsync(true, cancel.Token));
        Assert.Empty(f.Backend.Writes);
    }

    [Theory]
    [InlineData(null, 10d)]
    [InlineData(double.NaN, 10d)]
    [InlineData(-1d, 10d)]
    [InlineData(101d, 10d)]
    [InlineData(10d, 10d)]
    [InlineData(2d, null)]
    [InlineData(2d, double.NaN)]
    [InlineData(2d, double.PositiveInfinity)]
    [InlineData(2d, -1d)]
    [InlineData(2d, 20d)]
    public async Task BusyOrMissingTotalAndPowerImmediatelyRestore(double? cpu, double? power)
    {
        var f = await Fixture.Limited();
        var sample = f.Sample(cpu);
        sample = sample with { Devices = sample.Devices.Select(device => device.Kind == DeviceKind.Cpu ? device with { PowerWatts = power } : device).ToArray() };
        await f.Controller.ObserveAsync(sample, default);
        Assert.False(f.Controller.Status.IsLimited);
        Assert.Equal(f.Original, f.Backend.Values);
    }

    [Fact]
    public async Task MissingCpuRestoresAndNoValidSampleMakesFurtherWrites()
    {
        var f = await Fixture.Limited();
        await f.Controller.ObserveAsync(new(f.Clock.Now, []), default);
        Assert.Equal(f.Original, f.Backend.Values);
        var writes = f.Backend.Writes.Count;
        for (var i = 0; i < 400; i++)
        { f.Clock.Now = f.Clock.Now.AddSeconds(1); await f.Controller.ObserveAsync(new(f.Clock.Now, []), default); }
        Assert.Equal(writes, f.Backend.Writes.Count);
    }

    [Fact]
    public async Task NoGpuTelemetryNeverEstablishesIdle()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        for (var i = 0; i < 400; i++)
        {
            var sample = f.Sample();
            await f.Controller.ObserveAsync(sample with
            { Devices = sample.Devices.Where(device => device.Kind == DeviceKind.Cpu).ToArray() }, default);
            f.Clock.Now = f.Clock.Now.AddSeconds(1);
        }
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
        Assert.Contains("GPU", f.Controller.Status.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingKnownGpuRestoresAndCannotRestartIdleWithRemainingGpu(bool removeAll)
    {
        var f = await Fixture.Limited();
        for (var i = 0; i < 400; i++)
        {
            var sample = f.Sample();
            await f.Controller.ObserveAsync(sample with
            {
                Devices = sample.Devices.Where(device => device.Kind == DeviceKind.Cpu ||
                    (!removeAll && device.Kind == DeviceKind.IntegratedGpu)).ToArray()
            }, default);
            Assert.False(f.Controller.Status.IsLimited);
            Assert.Equal(f.Original, f.Backend.Values);
            Assert.Equal(0, f.Controller.Status.IdleSeconds);
            f.Clock.Now = f.Clock.Now.AddSeconds(1);
        }
        Assert.Null(f.Store.Lease);
    }

    [Fact]
    public async Task GpuSeenDuringBusyCpuStillMustBePresentForIdle()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Tick(cpu: 70);
        for (var i = 0; i < 400; i++)
        {
            var sample = f.Sample();
            await f.Controller.ObserveAsync(sample with
            { Devices = sample.Devices.Where(device => device.Kind != DeviceKind.DiscreteGpu).ToArray() }, default);
            f.Clock.Now = f.Clock.Now.AddSeconds(1);
        }
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
    }

    [Fact]
    public async Task ReturningKnownGpuRequiresFullNewIdleInterval()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(300);
        var missing = f.Sample();
        await f.Controller.ObserveAsync(missing with
        { Devices = missing.Devices.Where(device => device.Kind != DeviceKind.DiscreteGpu).ToArray() }, default);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        await f.Idle(600);
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(599, f.Controller.Status.IdleSeconds);
        await f.Tick();
        Assert.True(f.Controller.Status.IsLimited);
    }

    [Fact]
    public async Task NewlyDiscoveredGpuBreaksEstablishedIdleInterval()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(300);
        var sample = f.Sample();
        await f.Controller.ObserveAsync(sample with
        { Devices = [.. sample.Devices, new(DeviceKind.DiscreteGpu, "second dGPU", 0, 40, 0, 100)] }, default);
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
    }

    [Fact]
    public async Task IdenticalModelGpuCountReductionIsMissingTelemetry()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        var sample = f.Sample(cpu: 70);
        await f.Controller.ObserveAsync(sample with
        { Devices = [.. sample.Devices, sample.Devices.Last()] }, default);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        await f.Idle(400);
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
    }

    [Fact]
    public async Task FreshGpuSeenAfterSamplingGapRemainsRequired()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        var first = f.Sample();
        await f.Controller.ObserveAsync(first with
        { Devices = first.Devices.Where(device => device.Kind != DeviceKind.DiscreteGpu).ToArray() }, default);
        f.Clock.Now = f.Clock.Now.AddSeconds(4);
        await f.Controller.ObserveAsync(f.Sample(), default);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        for (var i = 0; i < 400; i++)
        {
            var sample = f.Sample();
            await f.Controller.ObserveAsync(sample with
            { Devices = sample.Devices.Where(device => device.Kind != DeviceKind.DiscreteGpu).ToArray() }, default);
            f.Clock.Now = f.Clock.Now.AddSeconds(1);
        }
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
    }

    [Fact]
    public async Task StableLimitedObservationsDoNotWriteOrRefreshEachSecond()
    {
        var f = await Fixture.Limited();
        var writes = f.Backend.Writes.Count;
        var refreshes = f.Backend.Refreshes;
        await f.Idle(1000);
        Assert.Equal(writes, f.Backend.Writes.Count);
        Assert.Equal(refreshes, f.Backend.Refreshes);
    }

    [Fact]
    public async Task CancellationAfterFirstWritePausesAndLeavesCrashRecoveryJournal()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(600);
        using var cancellation = new CancellationTokenSource();
        f.Backend.BeforeWrite = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Controller.ObserveAsync(f.Sample(), cancellation.Token));
        Assert.False(f.Controller.Status.Enabled);
        Assert.True(f.Controller.Status.HasRecoveryPending);
        Assert.NotNull(f.Store.Lease);
        Assert.Equal(3000u, f.Backend.Values.Class0Mhz);
        f.Backend.BeforeWrite = null;
        await f.Controller.RecoverAsync(default);
        Assert.Equal(f.Original, f.Backend.Values);
    }

    [Fact]
    public async Task InvalidJournalNeverReadsOrWritesPolicyAndCannotEnable()
    {
        var f = new Fixture();
        f.Store.Lease = new(3, new(Guid.Empty, true), f.Original, new(64000, 64000, 64000));
        await f.Controller.RecoverAsync(default);
        Assert.True(f.Controller.Status.HasRecoveryPending);
        await f.Controller.SetEnabledAsync(true, default);
        Assert.False(f.Controller.Status.Enabled);
        Assert.Equal(0, f.Backend.Reads);
        Assert.Empty(f.Backend.Writes);
        Assert.NotNull(f.Store.Lease);
    }

    [Fact]
    public async Task UnreadableJournalDoesNotGetDiscardedOrEnableAutomaticMode()
    {
        var f = new Fixture();
        f.Store.FailLoad = true;
        await f.Controller.RecoverAsync(default);
        Assert.True(f.Controller.Status.HasRecoveryPending);
        await f.Controller.SetEnabledAsync(true, default);
        Assert.False(f.Controller.Status.Enabled);
        Assert.Equal(0, f.Backend.Reads);
        Assert.Empty(f.Backend.Writes);
    }

    [Fact]
    public async Task ClearFailureRetainsRecoveryAndRetryDoesNotRewriteResolvedFields()
    {
        var f = await Fixture.Limited();
        f.Store.FailClear = true;
        await f.Controller.SetEnabledAsync(false, default);
        Assert.True(f.Controller.Status.HasRecoveryPending);
        Assert.Equal(f.Original, f.Backend.Values);
        var writes = f.Backend.Writes.Count;
        f.Store.FailClear = false;
        await f.Controller.RecoverAsync(default);
        Assert.Equal(writes, f.Backend.Writes.Count);
        Assert.False(f.Controller.Status.HasRecoveryPending);
    }

    [Fact]
    public async Task CanceledQueuedDisableDoesNotInterleaveWithAnActiveLeaseWrite()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(600);
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.BeforeSave = async () => { saving.SetResult(); await release.Task; };
        var applying = f.Controller.ObserveAsync(f.Sample(), default);
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var disable = f.Controller.SetEnabledAsync(false, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => disable);
        Assert.Empty(f.Backend.Writes);
        release.SetResult();
        await applying.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Controller.Status.IsLimited);
        await f.Controller.SetEnabledAsync(false, default);
        Assert.Equal(f.Original, f.Backend.Values);
    }

    [Fact]
    public async Task SlowJournalSaveCannotApplyCapUsingAnExpiredIdleSample()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(600);
        f.Store.BeforeSave = () => { f.Clock.Now = f.Clock.Now.AddSeconds(4); return Task.CompletedTask; };
        await f.Controller.ObserveAsync(f.Sample(), default);
        Assert.Empty(f.Backend.Writes);
        Assert.False(f.Controller.Status.IsLimited);
        Assert.Equal(0, f.Controller.Status.IdleSeconds);
        Assert.Null(f.Store.Lease);
    }

    [Fact]
    public async Task NewerBusyObservationDuringPersistencePreventsOutdatedCap()
    {
        var f = new Fixture();
        await f.Controller.SetEnabledAsync(true, default);
        await f.Idle(600);
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.BeforeSave = async () => { saving.SetResult(); await release.Task; };
        var applying = f.Controller.ObserveAsync(f.Sample(), default);
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        var busy = f.Controller.ObserveAsync(f.Sample(cpu: 70), default);
        release.SetResult();
        await Task.WhenAll(applying, busy).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(f.Backend.Writes);
        Assert.Equal(600, f.Controller.Status.IdleSeconds);
        Assert.Equal(99, f.Controller.Status.IdlePercent);
        Assert.Null(f.Store.Lease);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedBusySampleIsObservedAndRollingWindowCanReapplyAfterIdle(bool alreadyLimited)
    {
        var f = alreadyLimited ? await Fixture.Limited() : new Fixture();
        if (!alreadyLimited) { await f.Controller.SetEnabledAsync(true, default); await f.Idle(600); }
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Backend.BeforeContext = async () => { reading.TrySetResult(); await release.Task; };
        var firstIdle = f.Controller.ObserveAsync(f.Sample(), default);
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        var busy = f.Controller.ObserveAsync(f.Sample(cpu: 70), default);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        var lastIdle = f.Controller.ObserveAsync(f.Sample(), default);
        release.SetResult();
        await Task.WhenAll(firstIdle, busy, lastIdle).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Controller.Status.IsLimited);
        Assert.Equal(new DynamicFrequencyValues(3000, 2200, 3000), f.Backend.Values);
        Assert.Equal(600, f.Controller.Status.IdleSeconds);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 8, 31, 1, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture
    {
        public Clock Clock { get; } = new();
        public Backend Backend { get; } = new();
        public Store Store { get; } = new();
        public DynamicFrequencyValues Original { get; } = new(0, 2200, 5200);
        public DynamicPowerController Controller { get; }
        public Fixture() { Controller = new(Backend, Store, Clock); Backend.BeforeWrite = () => Assert.NotNull(Store.Lease); }
        public HardwareTelemetrySnapshot Sample(double? cpu = 2, double? core = 5, double? gpu = 0) => new(Clock.Now,
        [new(DeviceKind.Cpu, "CPU", cpu, 40, 10, 5000, MaximumCoreUsagePercent: core),
         new(DeviceKind.IntegratedGpu, "iGPU", 0, 40, 0, 100),
         new(DeviceKind.DiscreteGpu, "dGPU", gpu, 40, 0, 100)]);
        public async Task Tick(double? cpu = 2, double? core = 5, double? gpu = 0)
        { await Controller.ObserveAsync(Sample(cpu, core, gpu), default); Clock.Now = Clock.Now.AddSeconds(1); }
        public async Task Idle(int count) { for (var i = 0; i < count; i++) await Tick(); }
        public static async Task<Fixture> Limited() { var f = new Fixture(); await f.Controller.SetEnabledAsync(true, default); await f.Idle(601); return f; }
        public static async Task<Fixture> LimitedAfterRollingWindow()
        { var f = new Fixture(); await f.Controller.SetEnabledAsync(true, default); await f.Tick(); await f.Idle(600); return f; }
    }

    private sealed class Store : IDynamicPowerLeaseStore
    {
        public DynamicPowerLease? Lease;
        public bool FailSave;
        public bool FailLoad;
        public bool FailClear;
        public Func<Task>? BeforeSave;
        public Task<DynamicPowerLease?> LoadAsync(CancellationToken token)
        { if (FailLoad) throw new IOException("load failure"); return Task.FromResult(Lease); }
        public async Task SaveAsync(DynamicPowerLease lease, CancellationToken token)
        { if (BeforeSave is not null) await BeforeSave(); if (FailSave) throw new IOException("disk failure"); Lease = lease; }
        public Task ClearAsync(CancellationToken token) { if (FailClear) throw new IOException("clear failure"); Lease = null; return Task.CompletedTask; }
    }

    private sealed class Backend : IDynamicFrequencyBackend
    {
        public DynamicPowerContext Context = new(Guid.NewGuid(), true);
        public DynamicFrequencyValues Values = new(0, 2200, 5200);
        public int Reads;
        public int Refreshes;
        public List<uint> Writes { get; } = [];
        public int? FailWriteNumber;
        public Action? BeforeWrite;
        public Func<Task>? BeforeContext;
        public DynamicPowerContext? LastWriteContext;
        public async Task<DynamicPowerContext> GetContextAsync(CancellationToken token)
        { Reads++; if (BeforeContext is not null) await BeforeContext(); return Context; }
        public Task<DynamicFrequencyValues> ReadAsync(DynamicPowerContext context, CancellationToken token) { Reads++; return Task.FromResult(Values); }
        public Task WriteAsync(DynamicPowerContext context, int processorClass, uint mhz, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); BeforeWrite?.Invoke(); Writes.Add(mhz);
            if (Writes.Count == FailWriteNumber) throw new IOException("write failure");
            LastWriteContext = context;
            Values = processorClass switch { 0 => Values with { Class0Mhz = mhz }, 1 => Values with { Class1Mhz = mhz }, _ => Values with { Class2Mhz = mhz } };
            return Task.CompletedTask;
        }
        public Task RefreshAsync(DynamicPowerContext context, CancellationToken token) { Refreshes++; return Task.CompletedTask; }
    }
}
