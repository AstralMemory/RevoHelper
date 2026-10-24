using LibreHardwareMonitor.Hardware;
using RevoHelper.Core.Dashboard;

namespace RevoHelper.Hardware.Windows.Telemetry;

public static class LibreHardwareKindClassifier
{
    public static DeviceKind? Classify(HardwareType hardwareType, string name) =>
        hardwareType switch
        {
            HardwareType.Cpu => DeviceKind.Cpu,
            HardwareType.GpuNvidia => DeviceKind.DiscreteGpu,
            HardwareType.GpuIntel when name.Contains("Arc", StringComparison.OrdinalIgnoreCase) =>
                DeviceKind.DiscreteGpu,
            HardwareType.GpuIntel => DeviceKind.IntegratedGpu,
            HardwareType.GpuAmd when name.Contains(" RX ", StringComparison.OrdinalIgnoreCase) =>
                DeviceKind.DiscreteGpu,
            HardwareType.GpuAmd => DeviceKind.IntegratedGpu,
            _ => null
        };
}
