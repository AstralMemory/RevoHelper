using RevoHelper.Core.Hardware;
using RevoHelper.Core.Safety;

namespace RevoHelper.Core.Tests.Safety;

public sealed class HardwareAccessPolicyTests
{
    [Theory]
    [InlineData(HardwareOperation.ReadIdentity)]
    [InlineData(HardwareOperation.ReadSensors)]
    public void Evaluate_ReadOnlyOperation_IsAlwaysAllowed(HardwareOperation operation)
    {
        var context = new HardwareSafetyContext(
            MachineSupportStatus.Unsupported,
            AdapterVerified: false,
            FirmwareFanFallbackVerified: false);

        var result = HardwareAccessPolicy.Evaluate(operation, context);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Evaluate_WriteOnUnverifiedMachine_IsDenied()
    {
        var context = new HardwareSafetyContext(
            MachineSupportStatus.Unverified,
            AdapterVerified: true,
            FirmwareFanFallbackVerified: true);

        var result = HardwareAccessPolicy.Evaluate(HardwareOperation.WritePowerLimits, context);

        Assert.False(result.Allowed);
        Assert.Contains("machine", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_FanCurveWithoutFirmwareFallback_IsDenied()
    {
        var context = new HardwareSafetyContext(
            MachineSupportStatus.Supported,
            AdapterVerified: true,
            FirmwareFanFallbackVerified: false);

        var result = HardwareAccessPolicy.Evaluate(HardwareOperation.WriteFanCurve, context);

        Assert.False(result.Allowed);
        Assert.Contains("fallback", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_DirectFanDuty_IsAlwaysDenied()
    {
        var context = new HardwareSafetyContext(
            MachineSupportStatus.Supported,
            AdapterVerified: true,
            FirmwareFanFallbackVerified: true);

        var result = HardwareAccessPolicy.Evaluate(HardwareOperation.SetDirectFanDuty, context);

        Assert.False(result.Allowed);
    }

    [Theory]
    [InlineData(HardwareOperation.SwitchFirmwareProfile, HardwareOperation.WritePowerLimits)]
    [InlineData(HardwareOperation.WritePowerLimits, HardwareOperation.SetGpuMode)]
    [InlineData(HardwareOperation.SetGpuMode, HardwareOperation.SetRgb)]
    [InlineData(HardwareOperation.SetRgb, HardwareOperation.ControlLiquidCooling)]
    public void Evaluate_VerifiesEachWriteModuleIndependently(
        HardwareOperation verified,
        HardwareOperation unverified)
    {
        var context = new HardwareSafetyContext(
            MachineSupportStatus.Supported,
            AdapterVerified: true,
            FirmwareFanFallbackVerified: true,
            VerifiedOperations: new HashSet<HardwareOperation> { verified });

        Assert.True(HardwareAccessPolicy.Evaluate(verified, context).Allowed);
        Assert.False(HardwareAccessPolicy.Evaluate(unverified, context).Allowed);
    }
}
