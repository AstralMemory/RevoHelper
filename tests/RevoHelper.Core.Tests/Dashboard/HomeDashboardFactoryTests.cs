using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Tests.Dashboard;

public sealed class HomeDashboardFactoryTests
{
    private static readonly MachineIdentity Identity = new(
        "MECHREVO",
        "YAOSHI Series",
        "YAOSHI Series-X6AR55xY",
        "Intel(R) Core(TM) Ultra 9 275HX",
        "N.1.18MRO24",
        24,
        24);

    [Fact]
    public void Create_UsesDetectedCpuAndUnknownTelemetryPlaceholders()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, []);

        var cpu = Assert.Single(dashboard.Devices);
        Assert.Equal(DeviceKind.Cpu, cpu.Kind);
        Assert.Equal("Core Ultra 9 275HX", cpu.Model);
        Assert.Equal("24 核 · 24 线程", cpu.Details);
        Assert.Equal("N/A", cpu.Frequency);
        Assert.Equal("N/A", cpu.Usage);
        Assert.Equal("N/A", cpu.Temperature);
        Assert.Equal("N/A", cpu.Power);
    }

    [Fact]
    public void Create_AddsDetectedIntegratedAndDiscreteGpuRows()
    {
        var dashboard = HomeDashboardFactory.Create(
            Identity,
            ["Intel(R) Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"]);

        Assert.Collection(
            dashboard.Devices,
            cpu => Assert.Equal(DeviceKind.Cpu, cpu.Kind),
            discrete =>
            {
                Assert.Equal(DeviceKind.DiscreteGpu, discrete.Kind);
                Assert.Equal("GeForce RTX 5090 Laptop GPU", discrete.Model);
                Assert.Equal(string.Empty, discrete.Details);
            },
            integrated =>
            {
                Assert.Equal(DeviceKind.IntegratedGpu, integrated.Kind);
                Assert.Equal("Intel Graphics", integrated.Model);
                Assert.Equal(string.Empty, integrated.Details);
            });
    }

    [Fact]
    public void Create_RemovesDuplicateAndBlankDisplayAdapters()
    {
        var dashboard = HomeDashboardFactory.Create(
            Identity,
            ["NVIDIA GeForce RTX 5090 Laptop GPU", "", "nvidia geforce rtx 5090 laptop gpu"]);

        Assert.Equal(2, dashboard.Devices.Count);
        Assert.Equal(DeviceKind.DiscreteGpu, dashboard.Devices[1].Kind);
    }

    [Fact]
    public void Profiles_ExposeOfficeBalancedTurboAndCustomSlots()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, []);

        Assert.Collection(
            dashboard.Profiles,
            profile => Assert.Equal("办公", profile.DisplayName),
            profile => Assert.Equal("平衡", profile.DisplayName),
            profile => Assert.Equal("狂暴", profile.DisplayName),
            profile => Assert.Equal("自定义", profile.DisplayName));
    }

    [Fact]
    public void Create_LabelsUnknownPhysicalAdapterAsGpuInsteadOfGuessing()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, ["Example Graphics 9000"]);

        var gpu = dashboard.Devices[1];
        Assert.Equal(DeviceKind.UnknownGpu, gpu.Kind);
        Assert.Equal("GPU", gpu.Label);
    }

    [Theory]
    [InlineData("AMD Radeon(TM) 610M", "AMD Radeon 610M")]
    [InlineData("AMD Radeon 780M Graphics", "AMD Radeon 780M Graphics")]
    [InlineData("AMD Radeon(TM) 890M", "AMD Radeon 890M")]
    [InlineData("AMD Radeon(TM) Graphics", "AMD Radeon Graphics")]
    public void Create_LabelsEveryAmdIntegratedRadeonAsIntegrated(string windowsName, string shown)
    {
        var dashboard = HomeDashboardFactory.Create(Identity, [windowsName]);

        var gpu = dashboard.Devices[1];
        Assert.Equal(DeviceKind.IntegratedGpu, gpu.Kind);
        Assert.Equal("核显", gpu.Label);
        Assert.Equal(shown, gpu.Model);
    }

    [Fact]
    public void Create_KeepsRadeonRxDiscrete()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, ["AMD Radeon(TM) RX 7600M XT"]);

        Assert.Equal(DeviceKind.DiscreteGpu, dashboard.Devices[1].Kind);
    }

    [Fact]
    public void Create_LabelsIntelArcAsDiscreteInsteadOfMatchingTheIntelIntegratedRule()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, ["Intel(R) Arc(TM) B580 Graphics"]);

        var gpu = dashboard.Devices[1];
        Assert.Equal(DeviceKind.DiscreteGpu, gpu.Kind);
        Assert.Equal("独显", gpu.Label);
    }

    // V1.0.7: hardware facts decide, so a new product name needs no new rule.
    [Fact]
    public void CreateFromAdapters_TheBusPositionDecidesAnIntelGpuWhateverItsName()
    {
        var dashboard = HomeDashboardFactory.CreateFromAdapters(Identity,
        [
            new DisplayAdapter("Intel(R) Arc(TM) B390 GPU", HardwareVendor.Intel, Integrated: true),
            new DisplayAdapter("Intel(R) Arc(TM) A370M Graphics", HardwareVendor.Intel, Integrated: false)
        ]);

        Assert.Equal(DeviceKind.DiscreteGpu, dashboard.Devices[1].Kind);
        Assert.Equal("Intel Arc A370M Graphics", dashboard.Devices[1].Model);
        Assert.Equal(DeviceKind.IntegratedGpu, dashboard.Devices[2].Kind);
        Assert.Equal("核显", dashboard.Devices[2].Label);
        Assert.Equal(HardwareVendor.Intel, dashboard.Devices[2].Vendor);
    }

    [Theory]
    [InlineData("RTX A1000 Laptop GPU", HardwareVendor.Nvidia, DeviceKind.DiscreteGpu)]
    [InlineData("NVIDIA RTX PRO 2000 Blackwell Generation Laptop GPU", HardwareVendor.Nvidia, DeviceKind.DiscreteGpu)]
    [InlineData("AMD Radeon(TM) 8060S Graphics", HardwareVendor.Amd, DeviceKind.IntegratedGpu)]
    [InlineData("AMD Radeon RX 9070M", HardwareVendor.Amd, DeviceKind.DiscreteGpu)]
    [InlineData("Some Future Graphics", HardwareVendor.Amd, DeviceKind.IntegratedGpu)]
    [InlineData("Example Graphics 9000", HardwareVendor.Unknown, DeviceKind.UnknownGpu)]
    public void CreateFromAdapters_TheVendorFromThePciIdDecidesWithoutVendorWordsInTheName(
        string name, HardwareVendor vendor, DeviceKind expected)
    {
        var gpu = HomeDashboardFactory.CreateFromAdapters(Identity, [new DisplayAdapter(name, vendor)]).Devices[1];

        Assert.Equal(expected, gpu.Kind);
        Assert.Equal(vendor, gpu.Vendor);
    }

    [Fact]
    public void Create_NamesWithoutHardwareIdsStillCarryTheirVendor()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, ["NVIDIA RTX A1000 Laptop GPU", "Intel(R) Graphics"]);

        Assert.Equal(HardwareVendor.Nvidia, dashboard.Devices[1].Vendor);
        Assert.Equal(DeviceKind.DiscreteGpu, dashboard.Devices[1].Kind);
        Assert.Equal(HardwareVendor.Intel, dashboard.Devices[2].Vendor);
    }
}
