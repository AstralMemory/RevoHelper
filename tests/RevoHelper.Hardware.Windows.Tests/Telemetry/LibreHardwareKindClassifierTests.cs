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
    [InlineData(HardwareType.GpuIntel, "Intel(R) Arc(TM) A370M Graphics", DeviceKind.DiscreteGpu)]
    [InlineData(HardwareType.GpuIntel, "Intel Arc Graphics", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuIntel, "Intel(R) Arc(TM) 140V GPU", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuNvidia, "NVIDIA GeForce RTX 5090 Laptop GPU", DeviceKind.DiscreteGpu)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon Graphics", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon(TM) 610M", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon 780M Graphics", DeviceKind.IntegratedGpu)]
    [InlineData(HardwareType.GpuAmd, "AMD Radeon RX 7900M", DeviceKind.DiscreteGpu)]
    public void Classify_MapsSupportedCpuAndGpuHardware(
        HardwareType hardwareType,
        string name,
        DeviceKind expected)
    {
        Assert.Equal(expected, LibreHardwareKindClassifier.Classify(hardwareType, name));
    }
}
