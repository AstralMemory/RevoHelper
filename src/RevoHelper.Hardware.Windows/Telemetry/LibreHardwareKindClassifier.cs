using LibreHardwareMonitor.Hardware;
using RevoHelper.Core.Dashboard;

namespace RevoHelper.Hardware.Windows.Telemetry;

public static class LibreHardwareKindClassifier
{
    // The kind here only helps find the device's row when its name differs from Windows' own;
    // the row's kind comes from the PCI facts that Windows reports.
    public static DeviceKind? Classify(HardwareType hardwareType, string name, string? identifier = null) =>
        hardwareType switch
        {
            HardwareType.Cpu => DeviceKind.Cpu,
            HardwareType.GpuNvidia => DeviceKind.DiscreteGpu,
            // The library files Intel's integrated GPU under its own identifier (/gpu-intel-integrated/…).
            HardwareType.GpuIntel when identifier?.Contains("integrated", StringComparison.OrdinalIgnoreCase) == true =>
                DeviceKind.IntegratedGpu,
            HardwareType.GpuIntel when GpuNames.IsDiscreteIntelArc(name) =>
                DeviceKind.DiscreteGpu,
            HardwareType.GpuIntel => DeviceKind.IntegratedGpu,
            HardwareType.GpuAmd when name.Contains(" RX ", StringComparison.OrdinalIgnoreCase) =>
                DeviceKind.DiscreteGpu,
            HardwareType.GpuAmd => DeviceKind.IntegratedGpu,
            _ => null
        };

    /// <summary>The library knows which driver it talks to, so its type is the vendor.</summary>
    public static HardwareVendor Vendor(HardwareType hardwareType) => hardwareType switch
    {
        HardwareType.GpuNvidia => HardwareVendor.Nvidia,
        HardwareType.GpuAmd => HardwareVendor.Amd,
        HardwareType.GpuIntel => HardwareVendor.Intel,
        _ => HardwareVendor.Unknown
    };
}
