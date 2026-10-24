using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Safety;

public sealed record ControlledTransitionContext(
    MachineSupportStatus MachineStatus,
    bool ExactBiosSupported,
    bool AdapterVerified,
    bool? IsAcOnline,
    PlatformMode? CurrentMode,
    PlatformMode? RestoreMode,
    HardwareTelemetrySnapshot? Telemetry,
    bool HighLoadActive,
    bool GameOrStressProcessActive);

public sealed record TransitionPrerequisite(string Name, bool Passed, string Detail);

public sealed record ControlledTransitionDecision(
    bool Allowed,
    IReadOnlyList<TransitionPrerequisite> Checks);

public static class ControlledTransitionGate
{
    private const double MaximumSafeTemperatureC = 75;

    public static ControlledTransitionDecision Evaluate(ControlledTransitionContext context)
    {
        var checks = new List<TransitionPrerequisite>
        {
            Check("Machine", context.MachineStatus is MachineSupportStatus.Supported,
                "Exact model and BIOS must be explicitly supported."),
            Check("BIOS", context.ExactBiosSupported,
                "The exact BIOS version must be explicitly supported."),
            Check("Adapter", context.AdapterVerified,
                "The typed OEM platform-mode adapter must be verified."),
            Check("AC power", context.IsAcOnline is true,
                "AC power must be connected and readable."),
            Check("Current mode", context.CurrentMode is not null,
                "The current stock mode must be known."),
            Check("Restore mode", context.RestoreMode is not null,
                "A known restore mode is required."),
            Check("Workload", !context.HighLoadActive && !context.GameOrStressProcessActive,
                "No high-load sample, game, or stress process may be active.")
        };

        var cpu = context.Telemetry?.Devices.FirstOrDefault(device => device.Kind is DeviceKind.Cpu);
        var gpu = context.Telemetry?.Devices.FirstOrDefault(device =>
            device.Kind is DeviceKind.DiscreteGpu or DeviceKind.IntegratedGpu);
        var telemetryPresent = cpu?.TemperatureC is not null && gpu?.TemperatureC is not null;
        checks.Add(Check(
            "Telemetry",
            telemetryPresent,
            telemetryPresent
                ? "CPU and GPU temperatures are available."
                : "CPU and GPU temperature telemetry are both required."));
        var temperaturesSafe = telemetryPresent &&
            cpu!.TemperatureC < MaximumSafeTemperatureC &&
            gpu!.TemperatureC < MaximumSafeTemperatureC;
        checks.Add(Check(
            "Temperature",
            temperaturesSafe,
            $"CPU and GPU must both be below {MaximumSafeTemperatureC:0}°C."));

        return new(checks.All(check => check.Passed), checks);
    }

    private static TransitionPrerequisite Check(string name, bool passed, string detail) =>
        new(name, passed, detail);
}
