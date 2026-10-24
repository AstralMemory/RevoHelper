using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class RyzenAdjCpuPolicyControllerTests
{
    [Fact]
    public async Task Apply_SetsStapmAndSlowToPl1AndFastToPl2()
    {
        var client = new FakeRyzenAdjClient(new(45, 45, 65));
        var controller = new RyzenAdjCpuPolicyController(client);

        var result = await controller.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new RyzenAdjPowerLimits(35, 35, 55), client.Current);
        Assert.Equal(new CpuPolicy(35, 55, null), await controller.ReadCpuPolicyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Apply_WhenReadbackDiffers_RestoresAllOriginalAmdLimits()
    {
        var original = new RyzenAdjPowerLimits(45, 42, 65);
        var client = new FakeRyzenAdjClient(original) { IgnoreNextApply = true };
        var controller = new RyzenAdjCpuPolicyController(client);

        var result = await controller.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(original, client.Current);
        Assert.Equal(2, client.ApplyCount);
    }

    [Fact]
    public async Task Apply_WhenClientRejectsAfterPartialWrite_RestoresOriginalLimits()
    {
        var original = new RyzenAdjPowerLimits(45, 42, 65);
        var client = new FakeRyzenAdjClient(original) { RejectAfterNextWrite = true };
        var controller = new RyzenAdjCpuPolicyController(client);

        var result = await controller.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(original, client.Current);
        Assert.Equal(2, client.ApplyCount);
    }

    [Fact]
    public async Task Apply_WhenRollbackIsRejected_DoesNotClaimOriginalWasRestored()
    {
        var client = new FakeRyzenAdjClient(new(45, 42, 65))
        {
            RejectAfterNextWrite = true,
            RejectFollowingWrites = true
        };
        var result = await new RyzenAdjCpuPolicyController(client)
            .ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("已恢复", result.Reason);
        Assert.Contains("恢复", result.Reason);
    }

    [Fact]
    public async Task UnavailableClient_DoesNotExposeFakeReadback()
    {
        var client = new FakeRyzenAdjClient(new(45, 45, 65)) { IsAvailable = false };
        var controller = new RyzenAdjCpuPolicyController(client);

        Assert.Null(await controller.ReadCpuPolicyAsync(CancellationToken.None));
        Assert.False((await controller.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task PresetBaseline_ThroughSwitchableAndPlatformRouting_RestoresAllThreeDistinctLimits()
    {
        var original = new RyzenAdjPowerLimits(45.125m, 42.750m, 65.500m);
        var client = new FakeRyzenAdjClient(original);
        using var direct = new SwitchableCpuPolicyController();
        direct.SetController(new RyzenAdjCpuPolicyController(client));
        var controller = new PlatformAwareCpuPolicyController(new FakePlatformController(), new FakeOemCpuController(), direct);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None)).Succeeded);

        var restored = await controller.RestoreActivePresetBaselineAsync(CancellationToken.None);

        Assert.True(restored.Succeeded, restored.Reason);
        Assert.Equal(original, client.Current);
    }

    [Fact]
    public async Task ScheduleFailure_ThroughPlatformRouting_RestoresExactAmdLimits()
    {
        var original = new RyzenAdjPowerLimits(45.125m, 42.750m, 65.500m);
        var client = new FakeRyzenAdjClient(original);
        using var direct = new SwitchableCpuPolicyController();
        direct.SetController(new RyzenAdjCpuPolicyController(client));
        var platform = new FakePlatformController();
        var cpu = new PlatformAwareCpuPolicyController(platform, new FakeOemCpuController(), direct);
        var boost = new FakeBoostController();
        var controller = new PerformanceScheduleController(platform, cpu, boost, new FailingFrequencyController());

        var result = await controller.ApplyAsync(new(new(PlatformMode.Balanced), new(35, 55, null),
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), ProcessorFrequencyLimitPolicy.Uniform(3100)),
            platformAlreadySelected: true, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("已恢复", result.Reason);
        Assert.Equal(original, client.Current);
    }

    [Fact]
    public async Task VerifyWriteAccess_ReplaysTheExactThreeOriginalLimits()
    {
        var original = new RyzenAdjPowerLimits(45, 42, 65);
        var client = new FakeRyzenAdjClient(original);
        var controller = new RyzenAdjCpuPolicyController(client);

        var result = await controller.VerifyWriteAccessAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(original, client.Current);
        Assert.Equal(1, client.ApplyCount);
    }

    private sealed class FakePlatformController : IPlatformModeController
    {
        private PlatformModeSelection current = new(PlatformMode.Balanced);
        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(current);
        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken token)
        { current = selection; return ValueTask.FromResult(new ControlResult(true, "ok")); }
    }
    private sealed class FakeOemCpuController : ICpuPolicyController
    {
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken token) => ValueTask.FromResult<CpuPolicy?>(null);
        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken token) =>
            ValueTask.FromResult(ControlResult.Rejected("not custom"));
    }
    private sealed class FakeBoostController : IProcessorBoostController
    {
        private ProcessorBoostPolicy current = new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled);
        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(current);
        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy policy, CancellationToken token)
        { current = policy; return ValueTask.FromResult(new ControlResult(true, "ok")); }
    }
    private sealed class FailingFrequencyController : IProcessorFrequencyLimitController
    {
        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken token) =>
            ValueTask.FromResult(ProcessorFrequencyLimitPolicy.Uniform(0));
        public ValueTask<ControlResult> ApplyAsync(ProcessorFrequencyLimitPolicy policy, CancellationToken token) =>
            ValueTask.FromResult(policy.AcMaximumMhz == 0 ? new ControlResult(true, "restored")
                : ControlResult.Rejected("frequency write failed"));
    }

    private sealed class FakeRyzenAdjClient(RyzenAdjPowerLimits current) : IRyzenAdjClient
    {
        public bool IsAvailable { get; set; } = true;
        public string AvailabilityReason => IsAvailable ? "available" : "unavailable";
        public RyzenAdjPowerLimits Current { get; private set; } = current;
        public bool IgnoreNextApply { get; set; }
        public int ApplyCount { get; private set; }
        public bool RejectAfterNextWrite { get; set; }
        public bool RejectFollowingWrites { get; set; }

        public ValueTask<RyzenAdjPowerLimits?> ReadPowerLimitsAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<RyzenAdjPowerLimits?>(IsAvailable ? Current : null);

        public ValueTask<bool> ApplyPowerLimitsAsync(
            RyzenAdjPowerLimits limits,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            if (RejectFollowingWrites && ApplyCount > 1) return ValueTask.FromResult(false);
            if (IgnoreNextApply)
            {
                IgnoreNextApply = false;
            }
            else
            {
                Current = limits;
            }

            if (RejectAfterNextWrite)
            {
                RejectAfterNextWrite = false;
                return ValueTask.FromResult(false);
            }
            return ValueTask.FromResult(IsAvailable);
        }

        public void Dispose()
        {
        }
    }
}
