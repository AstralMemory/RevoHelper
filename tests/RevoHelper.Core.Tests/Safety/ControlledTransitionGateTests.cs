using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Safety;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Tests.Safety;

public sealed class ControlledTransitionGateTests
{
    [Fact]
    public void Evaluate_AllPrerequisitesPresent_AllowsTransition()
    {
        var result = ControlledTransitionGate.Evaluate(Context(
            cpuTemperature: 62,
            gpuTemperature: 51));

        Assert.True(result.Allowed);
        Assert.All(result.Checks, check => Assert.True(check.Passed));
    }

    [Fact]
    public void Evaluate_MissingCpuTemperature_BlocksTransition()
    {
        var result = ControlledTransitionGate.Evaluate(Context(
            cpuTemperature: null,
            gpuTemperature: 51));

        Assert.False(result.Allowed);
        Assert.Contains(result.Checks, check =>
            check.Name == "Telemetry" && !check.Passed && check.Detail.Contains("CPU", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_HighTemperatureOrActiveWorkload_BlocksTransition()
    {
        var context = Context(cpuTemperature: 76, gpuTemperature: 51) with
        {
            HighLoadActive = true,
            GameOrStressProcessActive = true
        };

        var result = ControlledTransitionGate.Evaluate(context);

        Assert.False(result.Allowed);
        Assert.Contains(result.Checks, check => check.Name == "Temperature" && !check.Passed);
        Assert.Contains(result.Checks, check => check.Name == "Workload" && !check.Passed);
    }

    [Fact]
    public void Evaluate_UnverifiedBios_BlocksTransition()
    {
        var result = ControlledTransitionGate.Evaluate(
            Context(cpuTemperature: 62, gpuTemperature: 51) with { ExactBiosSupported = false });

        Assert.False(result.Allowed);
        Assert.Contains(result.Checks, check => check.Name == "BIOS" && !check.Passed);
    }

    private static ControlledTransitionContext Context(double? cpuTemperature, double? gpuTemperature) =>
        new(
            MachineSupportStatus.Supported,
            ExactBiosSupported: true,
            AdapterVerified: true,
            IsAcOnline: true,
            CurrentMode: PlatformMode.Office,
            RestoreMode: PlatformMode.Office,
            new HardwareTelemetrySnapshot(
                DateTimeOffset.UtcNow,
                [
                    new DeviceTelemetry(DeviceKind.Cpu, "CPU", 5, cpuTemperature, 10, 2000),
                    new DeviceTelemetry(DeviceKind.DiscreteGpu, "GPU", 2, gpuTemperature, 8, 200)
                ]),
            HighLoadActive: false,
            GameOrStressProcessActive: false);
}
