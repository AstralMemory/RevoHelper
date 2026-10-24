using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class PerformanceScheduleControllerTests
{
    [Fact]
    public async Task ApplyAsync_AppliesPlatformThenBoostThenPowerAndVerifiesAllReadbacks()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Office), calls);
        var boost = new FakeBoostController(new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled), calls);
        var cpu = new FakeCpuController(new(35, 55, null), calls);
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Balanced),
            new(45, 65, null),
            new(ProcessorBoostMode.EfficientAggressive, ProcessorBoostMode.EfficientAggressive),
            ProcessorFrequencyLimitPolicy.Uniform(3100));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: false, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["read-platform", "read-boost", "read-cpu", "read-frequency", "set-platform", "read-cpu", "set-boost", "set-cpu", "set-frequency", "read-platform", "read-boost", "read-cpu", "read-frequency"], calls);
        Assert.Equal(target.Platform, result.Readback?.Platform);
        Assert.Equal(target.Boost, result.Readback?.Boost);
        Assert.Equal(target.Cpu, result.Readback?.Cpu);
        Assert.Equal(target.Frequency, result.Readback?.Frequency);
    }

    [Fact]
    public async Task ApplyAsync_WhenPlatformWasSelectedExternally_DoesNotEchoPlatformWrite()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Balanced), calls);
        var boost = new FakeBoostController(new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive), calls);
        var cpu = new FakeCpuController(new(45, 65, null), calls);
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Balanced),
            new(45, 65, null),
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled),
            ProcessorFrequencyLimitPolicy.Uniform(0));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: true, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.DoesNotContain("set-platform", calls);
        Assert.Contains("set-boost", calls);
    }

    [Fact]
    public async Task ApplyAsync_WhenEditingSelectedCustomProfile_ReassertsPlatformBeforePowerWrite()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Custom, 2), calls);
        var boost = new FakeBoostController(new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive), calls);
        var cpu = new FakeCpuController(new(75, 85, null), calls);
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Custom, 2),
            new(35, 36, null),
            new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive),
            ProcessorFrequencyLimitPolicy.Uniform(0));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: true, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.True(
            calls.IndexOf("set-platform") >= 0 && calls.IndexOf("set-platform") < calls.IndexOf("set-cpu"),
            $"Custom profile was not selected before the PL write: {string.Join(", ", calls)}");
    }

    [Fact]
    public async Task ApplyAsync_WhenUiSelectionIsStale_SelectsLiveTargetBeforePowerWrite()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Balanced), calls);
        var boost = new FakeBoostController(
            new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive),
            calls);
        var cpu = new FakeCpuController(new(45, 65, null), calls);
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Office),
            new(30, 40, null),
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled),
            ProcessorFrequencyLimitPolicy.Uniform(3000));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: true, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(target.Platform, platform.Current);
        Assert.True(
            calls.IndexOf("set-platform") >= 0 && calls.IndexOf("set-platform") < calls.IndexOf("set-cpu"),
            $"Live target was not selected before the PL write: {string.Join(", ", calls)}");
    }

    [Fact]
    public async Task ApplyAsync_WhenPowerFails_RestoresPlatformBoostAndPowerBaseline()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Office), calls);
        var originalBoost = new ProcessorBoostPolicy(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled);
        var boost = new FakeBoostController(originalBoost, calls);
        var originalCpu = new CpuPolicy(35, 55, null);
        var cpu = new FakeCpuController(originalCpu, calls) { RejectNextTarget = new(45, 65, null) };
        var originalFrequency = ProcessorFrequencyLimitPolicy.Uniform(2600);
        var frequency = new FakeFrequencyController(originalFrequency, calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Balanced),
            new(45, 65, null),
            new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive),
            ProcessorFrequencyLimitPolicy.Uniform(4100));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Office), await platform.ReadPlatformModeAsync(CancellationToken.None));
        Assert.Equal(originalBoost, await boost.ReadAsync(CancellationToken.None));
        Assert.Equal(originalCpu, await cpu.ReadCpuPolicyAsync(CancellationToken.None));
        Assert.Equal(originalFrequency, await frequency.ReadAsync(CancellationToken.None));
        Assert.Contains("已恢复", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyAsync_WithoutModelPowerOverride_KeepsOemPowerLoadedByPlatformMode()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Office), calls);
        var boost = new FakeBoostController(new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled), calls);
        var cpu = new FakeCpuController(new(35, 55, null), calls);
        platform.AfterSet = () => cpu.SetOemCurrent(new(62, 78, null));
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Balanced),
            null,
            new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive),
            ProcessorFrequencyLimitPolicy.Uniform(0));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: false, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new CpuPolicy(62, 78, null), result.Readback?.Cpu);
        Assert.Equal(0, cpu.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_WithoutPowerBackend_AppliesBoostAndFrequencyWhenCpuTargetIsNull()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Office), calls);
        var boost = new FakeBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled),
            calls);
        var cpu = new FakeCpuController(null, calls);
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls);
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Balanced),
            null,
            new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive),
            ProcessorFrequencyLimitPolicy.Uniform(4100));

        var result = await controller.ApplyAsync(target, platformAlreadySelected: false, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(target.Platform, result.Readback?.Platform);
        Assert.Null(result.Readback?.Cpu);
        Assert.Equal(target.Boost, result.Readback?.Boost);
        Assert.Equal(target.Frequency, result.Readback?.Frequency);
        Assert.Equal(0, cpu.ApplyCount);
    }

    [Fact]
    public async Task ApplyAsync_WhenLaterStepFails_RestoresTargetProfilePowerBeforeOriginalProfile()
    {
        var calls = new List<string>();
        var platform = new FakePlatformController(new(PlatformMode.Custom, 1), calls);
        var originalCpu = new CpuPolicy(35, 55, null);
        var targetBaselineCpu = new CpuPolicy(45, 65, null);
        var targetCpu = new CpuPolicy(75, 85, null);
        var cpu = new ProfileIndexedFakeCpuController(
            () => platform.Current,
            new Dictionary<int, CpuPolicy>
            {
                [1] = originalCpu,
                [2] = targetBaselineCpu
            },
            calls);
        var boost = new FakeBoostController(
            new(ProcessorBoostMode.EfficientEnabled, ProcessorBoostMode.EfficientEnabled),
            calls);
        var targetFrequency = ProcessorFrequencyLimitPolicy.Uniform(4100);
        var frequency = new FakeFrequencyController(ProcessorFrequencyLimitPolicy.Uniform(0), calls)
        {
            RejectNextTarget = targetFrequency
        };
        var controller = new PerformanceScheduleController(platform, cpu, boost, frequency);
        var target = new PerformanceSchedule(
            new(PlatformMode.Custom, 2),
            targetCpu,
            new(ProcessorBoostMode.Aggressive, ProcessorBoostMode.Aggressive),
            targetFrequency);

        var result = await controller.ApplyAsync(target, platformAlreadySelected: false, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Custom, 1), platform.Current);
        Assert.Equal(originalCpu, cpu.Values[1]);
        Assert.Equal(targetBaselineCpu, cpu.Values[2]);
        var targetRestore = calls.LastIndexOf("set-cpu:2");
        var originalRestore = calls.LastIndexOf("set-platform");
        Assert.True(
            targetRestore >= 0 && targetRestore < originalRestore,
            $"Target profile was not restored before returning to the original profile: {string.Join(", ", calls)}");
    }

    private sealed class FakePlatformController(PlatformModeSelection current, List<string> calls) : IPlatformModeController
    {
        public PlatformModeSelection Current => current;
        public Action? AfterSet { get; set; }
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken)
        {
            calls.Add("read-platform");
            return ValueTask.FromResult<PlatformModeSelection?>(current);
        }

        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken)
        {
            calls.Add("set-platform");
            current = selection;
            AfterSet?.Invoke();
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeBoostController(ProcessorBoostPolicy current, List<string> calls) : IProcessorBoostController
    {
        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken cancellationToken)
        {
            calls.Add("read-boost");
            return ValueTask.FromResult(current);
        }

        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy policy, CancellationToken cancellationToken)
        {
            calls.Add("set-boost");
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeCpuController(CpuPolicy? current, List<string> calls) : ICpuPolicyController
    {
        public CpuPolicy? RejectNextTarget { get; init; }
        public int ApplyCount { get; private set; }

        public void SetOemCurrent(CpuPolicy policy) => current = policy;

        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
        {
            calls.Add("read-cpu");
            return ValueTask.FromResult<CpuPolicy?>(current);
        }

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken)
        {
            calls.Add("set-cpu");
            ApplyCount++;
            if (policy == RejectNextTarget)
            {
                return ValueTask.FromResult(ControlResult.Rejected("simulated power failure"));
            }
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class ProfileIndexedFakeCpuController(
        Func<PlatformModeSelection> currentPlatform,
        Dictionary<int, CpuPolicy> values,
        List<string> calls) : ICpuPolicyController
    {
        public IReadOnlyDictionary<int, CpuPolicy> Values => values;

        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
        {
            var profile = currentPlatform().ProfileIndex;
            calls.Add($"read-cpu:{profile}");
            return ValueTask.FromResult<CpuPolicy?>(values[profile]);
        }

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(
            CpuPolicy policy,
            CancellationToken cancellationToken)
        {
            var profile = currentPlatform().ProfileIndex;
            calls.Add($"set-cpu:{profile}");
            values[profile] = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeFrequencyController(
        ProcessorFrequencyLimitPolicy current,
        List<string> calls) : IProcessorFrequencyLimitController
    {
        public ProcessorFrequencyLimitPolicy? RejectNextTarget { get; init; }

        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken)
        {
            calls.Add("read-frequency");
            return ValueTask.FromResult(current);
        }

        public ValueTask<ControlResult> ApplyAsync(
            ProcessorFrequencyLimitPolicy policy,
            CancellationToken cancellationToken)
        {
            calls.Add("set-frequency");
            if (policy == RejectNextTarget)
            {
                return ValueTask.FromResult(ControlResult.Rejected("simulated frequency failure"));
            }
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }
}
