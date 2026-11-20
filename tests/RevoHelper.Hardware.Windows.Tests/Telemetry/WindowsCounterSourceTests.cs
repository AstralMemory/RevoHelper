using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class WindowsCounterSourceTests
{
    // This machine (2026-11-16): \_SB.ECTZ and \_TZ.TZ00 both read 3392, i.e. 66 °C.
    [Fact]
    public void ThermalZonesAreTenthsOfAKelvinAndTheHottestPlausibleOneWins()
    {
        Assert.Equal(66.1, PdhWindowsCounterSource.HottestZone([3392, 3392]));
        Assert.Equal(71.9, PdhWindowsCounterSource.HottestZone([3392, 3450]));
    }

    [Fact]
    public void PlaceholderZonesAreNotTakenForACpuTemperature()
    {
        // 27.8 °C is the fixed value many boards report; 0 K and absurd values are broken zones.
        Assert.Null(PdhWindowsCounterSource.HottestZone([3010]));
        Assert.Null(PdhWindowsCounterSource.HottestZone([0, 99999]));
        Assert.Null(PdhWindowsCounterSource.HottestZone([]));
        Assert.Equal(66.1, PdhWindowsCounterSource.HottestZone([3010, 3392]));
    }

    // Energy Meter instances on this machine; the meter reports milliwatts.
    [Fact]
    public void ThePackageAndGraphicsPlanesComeFromTheirRaplDomains()
    {
        (string, double)[] meters =
        [
            ("rapl_package0_dram", 0),
            ("rapl_package0_pkg", 35139.18),
            ("rapl_package0_pp0", 24588.01),
            ("rapl_package0_pp1", 350.98),
            ("_total", 0)
        ];

        Assert.Equal(35.1, PdhWindowsCounterSource.SumMilliwatts(meters, "_pkg"));
        Assert.Equal(0.4, PdhWindowsCounterSource.SumMilliwatts(meters, "_pp1"));
        Assert.Null(PdhWindowsCounterSource.SumMilliwatts(meters, "_dram"));
        Assert.Null(PdhWindowsCounterSource.SumMilliwatts([], "_pkg"));
        Assert.Equal(60, PdhWindowsCounterSource.SumMilliwatts(
            [("rapl_package0_pkg", 30000), ("rapl_package1_pkg", 30000)], "_pkg"));
    }

    [Fact]
    public void UsageFromSystemTimesNeedsTwoReadsAndStaysWithinRange()
    {
        Assert.Null(SystemTimesUsage.Usage(0, 0, 500, 1000));
        Assert.Equal(25, SystemTimesUsage.Usage(500, 1000, 1250, 2000));
        Assert.Equal(0, SystemTimesUsage.Usage(500, 1000, 1500, 2000));
        Assert.Null(SystemTimesUsage.Usage(500, 1000, 400, 2000));
        Assert.Null(SystemTimesUsage.Usage(500, 1000, 600, 1000));
    }

    // Reads Windows' own counters on whatever machine runs the tests; nothing is written.
    [Fact]
    public void ReadingTheRealCountersNeverThrows()
    {
        using var source = new PdhWindowsCounterSource();
        var first = source.Read();
        Thread.Sleep(200);
        var second = source.Read();

        Assert.NotNull(first);
        Assert.True(second.CpuUsagePercent is null or (>= 0 and <= 100));
        Assert.True(second.CpuFrequencyMhz is null or > 0);
    }
}
