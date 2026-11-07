using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Tests.Presentation;

public sealed class Snapshot1V104SimplificationTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task NewClick_EndsTheOldRequestsFixedWaitAndStillRemovesTheOfficeCap()
    {
        var platform = new Platform();
        var frequency = new Frequency(3400);
        var waitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waits = 0;
        var vm = await Create(platform, frequency, OfficeCapSettings(), (_, token) =>
        {
            if (Interlocked.Increment(ref waits) != 1) return Task.CompletedTask;
            waitEntered.TrySetResult();
            return Task.Delay(Timeout.Infinite, token);
        });

        var first = vm.ApplyPlatformProfileAsync("balanced", default);
        await waitEntered.Task.WaitAsync(Limit);
        // The cap is already gone before the recheck wait starts.
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.Current);
        Assert.False(first.IsCompleted);

        var second = vm.ApplyPlatformProfileAsync("turbo", default);

        Assert.True(await first.WaitAsync(Limit));
        Assert.True(await second.WaitAsync(Limit), vm.StatusMessage);
        Assert.Equal(PlatformMode.TurboQuiet, platform.Current.Mode);
        Assert.Equal(PlatformMode.TurboQuiet, vm.ConfirmedPlatformSelection?.Mode);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), frequency.Current);
    }

    [Fact]
    public async Task WithoutNewClick_TheFixedWaitRunsToTheEnd()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = await Create(new Platform(), new Frequency(3400), OfficeCapSettings(), (_, _) =>
        {
            waitEntered.TrySetResult();
            return release.Task;
        });

        var switching = vm.ApplyPlatformProfileAsync("balanced", default);
        await waitEntered.Task.WaitAsync(Limit);
        await Task.Delay(50);
        Assert.False(switching.IsCompleted);

        release.SetResult();

        Assert.True(await switching.WaitAsync(Limit), vm.StatusMessage);
    }

    [Fact]
    public async Task CancellingTheCaller_StillCancelsTheFixedWait()
    {
        var waitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var vm = await Create(new Platform(), new Frequency(3400), OfficeCapSettings(), (_, token) =>
        {
            waitEntered.TrySetResult();
            return Task.Delay(Timeout.Infinite, token);
        });

        var switching = vm.ApplyPlatformProfileAsync("balanced", cancellation.Token);
        await waitEntered.Task.WaitAsync(Limit);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => switching.WaitAsync(Limit));
    }

    [Fact]
    public async Task StartupSync_DoesNotClaimAPreviewOrReplaceSensorStatus()
    {
        var vm = await Create(new Platform(), new Frequency(0), new(), (_, _) => Task.CompletedTask);

        Assert.False(vm.HasOperationFeedback);
        Assert.DoesNotContain("界面预览", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusEcho_KeepsTheSwitchResultMessage()
    {
        var platform = new Platform();
        var vm = await Create(platform, new Frequency(0), new(), (_, _) => Task.CompletedTask);
        Assert.True(await vm.ApplyPlatformProfileAsync("balanced", default), vm.StatusMessage);
        var result = vm.StatusMessage;

        Assert.True(await vm.ObservePlatformModeAsync(platform.Current, default));

        Assert.Equal(result, vm.StatusMessage);
    }

    [Fact]
    public async Task ExternalModeChange_ConfirmsARewrittenFrequencyInsteadOfAssumingIt()
    {
        var kept = new Frequency(3400) { IgnoreWrites = true };
        var vm = await Create(new Platform(), kept, new(), (_, _) => Task.CompletedTask);

        Assert.False(await vm.ObservePlatformModeAsync(new(PlatformMode.Balanced), default));
        Assert.Contains("频率仍被覆盖", vm.StatusMessage, StringComparison.Ordinal);

        var accepted = new Frequency(3400);
        vm = await Create(new Platform(), accepted, new(), (_, _) => Task.CompletedTask);

        Assert.True(await vm.ObservePlatformModeAsync(new(PlatformMode.Balanced), default));
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), accepted.Current);
    }

    [Fact]
    public async Task ManualPreview_StillSaysNothingWasSent()
    {
        var vm = await Create(new Platform(), new Frequency(0), new(), (_, _) => Task.CompletedTask);

        vm.SelectProfile("balanced");

        Assert.Contains("界面预览", vm.StatusMessage, StringComparison.Ordinal);
    }

    private static ApplicationSettings OfficeCapSettings() => new(PerformanceProfiles: new()
    {
        ["office:1"] = new(75, 85, ProcessorBoostMode.EfficientEnabled, 3400, true)
    });

    // Only Office is reported as supported, so the other modes take the mode-only route.
    private static async Task<MainWindowViewModel> Create(Platform platform, Frequency frequency,
        ApplicationSettings settings, Func<TimeSpan, CancellationToken, Task> delay)
    {
        var vm = new MainWindowViewModel(
            HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "Test Series", "Test Board", "Intel CPU", "test", 8, 16), []),
            settings, new Store(), new Startup(),
            cpuPolicyController: new Cpu(), boostController: new Boost(),
            platformModeController: platform, frequencyLimitController: frequency,
            verificationDelay: delay);
        vm.ApplyRuntimeCapabilities(new(vm.Dashboard.Identity,
            new Dictionary<HardwareFeature, HardwareFeatureCapability>(),
            new HashSet<PlatformMode> { PlatformMode.Office }, []));
        await vm.InitializePlatformModeAsync(default);
        return vm;
    }

    private sealed class Store : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }

    private sealed class Startup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }

    private sealed class Platform : IPlatformModeController
    {
        public PlatformModeSelection Current { get; private set; } = new(PlatformMode.Office);
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(Current);
        public ValueTask<ControlResult> SetBasicTurboModeAsync(int index, CancellationToken token)
        {
            Current = new(PlatformMode.TurboQuiet, index);
            return ValueTask.FromResult(new ControlResult(true, "basic turbo confirmed") { ConfirmedPlatform = Current });
        }
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection target, CancellationToken token)
        {
            Current = target;
            return ValueTask.FromResult(new ControlResult(true, "mode confirmed") { ConfirmedPlatform = target });
        }
    }

    private sealed class Cpu : ICpuPolicyController
    {
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken token) =>
            ValueTask.FromResult<CpuPolicy?>(new(75, 85, null));
        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy value, CancellationToken token) =>
            throw new InvalidOperationException("Mode-only switching must not write CPU power limits");
    }

    private sealed class Boost : IProcessorBoostController
    {
        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken token) =>
            ValueTask.FromResult(new ProcessorBoostPolicy(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive));
        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy value, CancellationToken token) =>
            throw new InvalidOperationException("Mode-only switching must not write Boost");
    }

    private sealed class Frequency(uint initial) : IProcessorFrequencyLimitController
    {
        public ProcessorFrequencyLimitPolicy Current { get; private set; } = ProcessorFrequencyLimitPolicy.Uniform(initial);
        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(Current);
        public bool IgnoreWrites { get; init; }
        public ValueTask<ControlResult> ApplyAsync(ProcessorFrequencyLimitPolicy value, CancellationToken token)
        {
            if (!IgnoreWrites) Current = value;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }
}
