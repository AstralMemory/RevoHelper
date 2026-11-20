using RevoHelper.Core.Dashboard;
using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class WindowsGpuSourceTests
{
    // "GPU Engine" instance names as this machine reports them (2026-11-16); one per process and engine.
    [Fact]
    public void EachEngineAddsUpItsProcessesAndTheBusiestEngineIsTheUsage()
    {
        (string, double)[] instances =
        [
            ("pid_4120_luid_0x00000000_0x000108F0_phys_0_eng_0_engtype_3D", 20),
            ("pid_9876_luid_0x00000000_0x000108F0_phys_0_eng_0_engtype_3D", 8.2),
            ("pid_9876_luid_0x00000000_0x000108f0_phys_0_eng_4_engtype_Copy", 34),
            ("pid_9876_luid_0x00000000_0x000108F0_phys_0_eng_12_engtype_Copy", 2.2),
            ("pid_1500_luid_0x00000000_0x000103A5_phys_0_eng_0_engtype_3D", 33.8),
            ("pid_1500_luid_0x00000000_0x000103A5_phys_0_eng_5_engtype_Compute", 0),
            ("pid_1500_luid_0x00000000_0x000103A5_phys_0_eng_7_engtype_", 0),
            ("_Total", 50),
            ("pid_1_luid_0x00000000_0x000139DA_phys_0_eng_0_engtype_Neural", double.NaN)
        ];

        var engines = GpuEngineUsage.Aggregate(instances);

        Assert.Equal(34, engines["luid_0x00000000_0x000108F0"].UsagePercent);
        Assert.Equal([(0, 0)], engines["luid_0x00000000_0x000108F0"].ThreeDNodes);
        Assert.Equal(33.8, engines["luid_0x00000000_0x000103A5"].UsagePercent);
        Assert.Equal(2, engines.Count);
    }

    [Fact]
    public void AnEngineSharedByManyProcessesNeverShowsMoreThanAHundred()
    {
        var engines = GpuEngineUsage.Aggregate(
        [
            ("pid_1_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 70),
            ("pid_2_luid_0x00000000_0x00000001_phys_0_eng_0_engtype_3D", 40)
        ]);

        Assert.Equal(100, engines["luid_0x00000000_0x00000001"].UsagePercent);
    }

    [Fact]
    public void TheLuidIsSpelledTheWayTheCountersSpellIt()
    {
        Assert.Equal("luid_0x00000000_0x000108F0", GpuEngineUsage.LuidKey(0x000108F0, 0));
        Assert.Equal("luid_0x00000001_0x0000ABCD", GpuEngineUsage.LuidKey(0xABCD, 1));
    }

    // This machine's RTX 5090 reported 538 (53.8 °C) and 1 627 000 000 Hz, matching its own sensors.
    [Fact]
    public void DriverFiguresAreConvertedAndZeroMeansNone()
    {
        Assert.Equal(53.8, WddmGpuSource.TemperatureFromTenths(538));
        Assert.Null(WddmGpuSource.TemperatureFromTenths(0));
        Assert.Null(WddmGpuSource.TemperatureFromTenths(2000));
        Assert.Equal(1627, WddmGpuSource.FrequencyFromHertz(1_627_000_000));
        Assert.Equal(550, WddmGpuSource.FrequencyFromHertz(550_000_000));
        Assert.Null(WddmGpuSource.FrequencyFromHertz(0));
        Assert.Null(WddmGpuSource.FrequencyFromHertz(25_000_000_000));
    }

    [Fact]
    public void AnAdaptersKindComesFromTheSameFactsAsItsRow()
    {
        PciLocation? Location(string id) => id.Contains("VEN_8086", StringComparison.Ordinal) ? new(0, 2, 0) : new(1, 0, 0);

        var intel = WddmGpuSource.Describe(@"PCI\VEN_8086&DEV_B080&SUBSYS_00000000&REV_00\3&1", "Intel(R) Arc(TM) B390 GPU", Location);
        var nvidia = WddmGpuSource.Describe(@"PCI\VEN_10DE&DEV_2C18&SUBSYS_60401D05&REV_A1\4&1", "NVIDIA GeForce RTX 5090 Laptop GPU", Location);

        Assert.Equal((HardwareVendor.Intel, DeviceKind.IntegratedGpu), (intel.Vendor, intel.Kind));
        Assert.Equal((HardwareVendor.Nvidia, DeviceKind.DiscreteGpu), (nvidia.Vendor, nvidia.Kind));
    }

    // Reads Windows' own GPU data on whatever machine runs the tests; nothing is written, and a
    // powered-down adapter's driver is not asked.
    [Fact]
    public void ReadingTheRealGpuDataNeverThrows()
    {
        using var source = new WddmGpuSource();
        var adapters = source.Adapters();
        var powered = adapters.Where(adapter => source.ReadPowered(adapter) == true).ToArray();
        source.ReadUsage(powered);
        Thread.Sleep(500);
        var usage = source.ReadUsage(powered);

        Assert.All(usage.Values, value => Assert.InRange(value, 0, 100));
        foreach (var adapter in powered)
        {
            var reading = source.ReadDriver(adapter);
            Assert.True(reading.TemperatureC is null or (> 0 and < 150));
            Assert.True(reading.FrequencyMhz is null or (> 0 and < 20000));
            Assert.True(source.ReadDedicatedMemoryMegabytes(adapter) is null or > 0);
        }
    }
}
