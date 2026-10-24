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

    [Fact]
    public void Create_LabelsIntelArcAsDiscreteInsteadOfMatchingTheIntelIntegratedRule()
    {
        var dashboard = HomeDashboardFactory.Create(Identity, ["Intel(R) Arc(TM) B580 Graphics"]);

        var gpu = dashboard.Devices[1];
        Assert.Equal(DeviceKind.DiscreteGpu, gpu.Kind);
        Assert.Equal("独显", gpu.Label);
    }
}
