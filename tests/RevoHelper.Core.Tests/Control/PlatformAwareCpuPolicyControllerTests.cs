using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class PlatformAwareCpuPolicyControllerTests
{
    [Fact]
    public async Task CustomProfile_UsesOemProfileController()
    {
        var platform = new FakePlatformController(new(PlatformMode.Custom, 2));
        var oem = new FakeCpuController(new(75, 85, null));
        var direct = new FakeCpuController(new(45, 65, null));
        var controller = new PlatformAwareCpuPolicyController(platform, oem, direct);

        var result = await controller.ApplyCpuPolicyAsync(new(35, 36, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, oem.ApplyCount);
        Assert.Equal(0, direct.ApplyCount);
    }

    [Theory]
    [InlineData(PlatformMode.Office)]
    [InlineData(PlatformMode.Balanced)]
    [InlineData(PlatformMode.TurboQuiet)]
    [InlineData(PlatformMode.TurboHigh)]
    public async Task PresetProfile_UsesDirectCpuBackend(PlatformMode mode)
    {
        var platform = new FakePlatformController(new(mode));
        var oem = new FakeCpuController(new(75, 85, null));
        var direct = new FakeCpuController(new(45, 65, null));
        var controller = new PlatformAwareCpuPolicyController(platform, oem, direct);

        var result = await controller.ApplyCpuPolicyAsync(new(35, 36, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(0, oem.ApplyCount);
        Assert.Equal(1, direct.ApplyCount);
    }

    [Fact]
    public async Task PresetProfile_WithoutVerifiedDirectBackend_IsRejected()
    {
        var platform = new FakePlatformController(new(PlatformMode.Balanced));
        var controller = new PlatformAwareCpuPolicyController(
            platform,
            new FakeCpuController(new(75, 85, null)),
            directController: null);

        var result = await controller.ApplyCpuPolicyAsync(new(35, 36, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("直连", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SwitchableBackend_IsReportedUnavailableUntilAControllerIsInstalled()
    {
        using var direct = new SwitchableCpuPolicyController();
        var controller = new PlatformAwareCpuPolicyController(
            new FakePlatformController(new(PlatformMode.Balanced)),
            new FakeCpuController(new(75, 85, null)),
            direct);

        Assert.False(controller.CanControlPresetPowerLimits);

        direct.SetController(new FakeCpuController(new(45, 65, null)));

        Assert.True(controller.CanControlPresetPowerLimits);
    }

    [Fact]
    public async Task Readback_FollowsTheCurrentlyActiveMode()
    {
        var platform = new FakePlatformController(new(PlatformMode.Office));
        var oem = new FakeCpuController(new(75, 85, null));
        var direct = new FakeCpuController(new(35, 36, null));
        var controller = new PlatformAwareCpuPolicyController(platform, oem, direct);

        Assert.Equal(new CpuPolicy(35, 36, null), await controller.ReadCpuPolicyAsync(CancellationToken.None));

        platform.Current = new(PlatformMode.Custom, 1);
        Assert.Equal(new CpuPolicy(75, 85, null), await controller.ReadCpuPolicyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RestoreActivePresetBaseline_RestoresTheHardwareValueCapturedBeforeFirstOverride()
    {
        var platform = new FakePlatformController(new(PlatformMode.Balanced));
        var direct = new FakeCpuController(new(85, 95, null));
        var controller = new PlatformAwareCpuPolicyController(
            platform,
            new FakeCpuController(new(75, 85, null)),
            direct);

        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), CancellationToken.None)).Succeeded);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(40, 50, null), CancellationToken.None)).Succeeded);

        var restore = await controller.RestoreActivePresetBaselineAsync(CancellationToken.None);

        Assert.True(restore.Succeeded, restore.Reason);
        Assert.Equal(new CpuPolicy(85, 95, null), await direct.ReadCpuPolicyAsync(CancellationToken.None));
        Assert.Equal(3, direct.ApplyCount);
    }

    [Fact]
    public async Task RestoreActivePresetBaseline_DoesNotWriteInCustomMode()
    {
        var platform = new FakePlatformController(new(PlatformMode.Custom, 1));
        var direct = new FakeCpuController(new(85, 95, null));
        var controller = new PlatformAwareCpuPolicyController(
            platform,
            new FakeCpuController(new(75, 85, null)),
            direct);

        var restore = await controller.RestoreActivePresetBaselineAsync(CancellationToken.None);

        Assert.True(restore.Succeeded, restore.Reason);
        Assert.Equal(0, direct.ApplyCount);
    }

    private sealed class FakePlatformController(PlatformModeSelection current) : IPlatformModeController
    {
        public PlatformModeSelection Current { get; set; } = current;

        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<PlatformModeSelection?>(Current);

        public ValueTask<ControlResult> SetPlatformModeAsync(
            PlatformModeSelection selection,
            CancellationToken cancellationToken)
        {
            Current = selection;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeCpuController(CpuPolicy current) : ICpuPolicyController
    {
        public int ApplyCount { get; private set; }

        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CpuPolicy?>(current);

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(
            CpuPolicy policy,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }
}
