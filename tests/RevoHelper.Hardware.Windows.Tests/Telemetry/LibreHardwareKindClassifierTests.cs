using LibreHardwareMonitor.Hardware;
using RevoHelper.Core.Dashboard;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class LibreHardwareKindClassifierTests
{
    [Theory]
    [InlineData(HardwareType.Cpu, "Intel Core Ultra 9 275HX", DeviceKind.Cpu)]
    [InlineData(HardwareType.GpuIntel, "Intel Graphics", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuIntel, "Intel Arc A770", DeviceKind.DiscreteGpu)]
    [InlineData(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 5090 Laptop GPU", DeviceKind.DiscreteGpu)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon Graphics", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon RX 7900M", DeviceKind.DiscreteGpu)]
    public void Classify_MapsSupportedCpuAndGpuHardware(
        HardwareType hardwareType,
        string name,
        DeviceKind expected)
    {
        Assert.Equal(expected, LibreHardwareKindClassifier.Classify(hardwareType, name));
    }
}
