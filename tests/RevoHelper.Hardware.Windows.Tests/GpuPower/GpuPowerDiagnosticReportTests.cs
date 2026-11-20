using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.GpuPower;

namespace RevoHelper.Hardware.Windows.Tests.GpuPower;

public sealed class GpuPowerDiagnosticReportTests
{
    private static readonly NvidiaPowerReading Rtx5090 = new("NVIDIA GeForce RTX 5090 Laptop GPU", "581.57", "98.02.2E.00.0B",
        "P8", "45", "12.34", "N/A", "150.00", "95.00", "5.00", "175.00", "210", "3090");

    private static readonly GpuDiagnosticMachine Machine = new("MECHREVO", "Model X", "", "X6AR5xx", "N.1.18MRO24", "1.18",
        "Intel(R) Core(TM) Ultra 9 275HX", "10.0.26300.0");

    private const string OemPayload = """
        {"OperatingMode":"0","GPU_ConfigurableTGPSwitch":"1","GPU_ConfigurableTGPTarget":"130",
         "GPU_ConfigurableTGPMinimum":"95","GPU_ConfigurableTGPMaximum":"150",
         "GPU_DynamicBoostSwitch":"1","GPU_DynamicBoost":"20","GPU_DynamicBoostMinimum":"5","GPU_DynamicBoostMaximum":"25",
         "GPU_CoreClockOffsetOC":"100"}
        """;

    [Fact]
    public void ThisMachineInOfficeModeReadsAsItsDriverLimit()
    {
        var report = GpuPowerDiagnosticFormatter.Format(Snapshot(Readout(GpuPowerEcReaderTests.FakeBackend.Office)));
        var lines = report.Text.Split("\r\n");

        Assert.Equal("RevoHelper 显卡功耗诊断 · 1.0.7-beta.2+20261116 · 2026-11-16 20:30", lines[0]);
        Assert.Contains("机型：MECHREVO Model X（主板 X6AR5xx） · BIOS N.1.18MRO24 · EC 1.18", lines);
        Assert.Contains("模式：办公 · 接电", lines);
        Assert.Contains("显卡：NVIDIA GeForce RTX 5090 Laptop GPU · 驱动 581.57 · VBIOS 98.02.2E.00.0B", lines);
        Assert.Contains("驱动功耗上限（W）：生效 150 · 设定 N/A · 默认 95 · 最小 5 · 最大 175 · 当前功耗 12.34", lines);
        Assert.Contains("NVPCF：正常（NVIDIA Platform Controllers and Framework）", lines);
        Assert.Contains("EC 门控：0490=07 0456=C8 0741=01 0727=88 072B=01 07C4=38 07C7=0E 07C8=60 0A6E=00 07D2=1B", lines);
        Assert.Contains("EC 下发/镜像：09EA=32/0788=32 09EB=00/07D4=00 09EC=05/07D5=05 07F7=CD/9062=CD 0763=3C 0764=3C 07D6=00 07D7=00", lines);
        Assert.Contains("9190 32 05 32 05 2D 2D 37 00", lines);
        Assert.Contains("91C0 32 05 32 05 3C 3C CD 00 ← 当前", lines);
        Assert.Contains("91E0 37 19 37 19 5A 5A C8 00", lines);
        Assert.Contains("当前行（推断）：91C0（07C8 bit5=1，0A6E=00）", lines);
        Assert.Contains("固件下发：基础 +50 W，动态加速 +5 W，与当前行一致", lines);
        Assert.Contains("推算本档上限：默认 95 + 50 + 5 = 150 W（显卡最大 175 W）", lines);
        Assert.DoesNotContain(lines, line => line.StartsWith("注意：", StringComparison.Ordinal));
        Assert.Equal("驱动生效上限 150 W，显卡最大 175 W。", report.Summary);
    }

    [Fact]
    public void ARowThatDiffersFromWhatTheFirmwarePassesOnIsShown()
    {
        var bytes = GpuPowerEcReaderTests.FakeBackend.Office.ToDictionary();
        bytes[0x09EA] = 0x37;
        bytes[0x09EC] = 0x19;

        var text = GpuPowerDiagnosticFormatter.Format(Snapshot(Readout(bytes))).Text;

        Assert.Contains("固件下发：基础 +55 W，动态加速 +25 W，当前行为 +50 / +5 W", text);
        Assert.Contains("推算本档上限：默认 95 + 55 + 25 = 175 W（显卡最大 175 W）", text);
    }

    [Fact]
    public void AnUnknownRowOffsetIsNotGuessed()
    {
        var bytes = GpuPowerEcReaderTests.FakeBackend.Office.ToDictionary();
        bytes[0x0A6E] = 0x30;

        var text = GpuPowerDiagnosticFormatter.Format(Snapshot(Readout(bytes))).Text;

        Assert.Contains("当前行（推断）：无法确定（07C8=60，0A6E=30）", text);
        Assert.DoesNotContain("← 当前", text);
        Assert.Contains("固件下发：基础 +50 W，动态加速 +5 W\r\n", text);
    }

    [Fact]
    public void OnBatteryAndInTheCustomBranchTheReportSaysSo()
    {
        var bytes = GpuPowerEcReaderTests.FakeBackend.Office.ToDictionary();
        bytes[0x0490] = 0x06;
        bytes[0x0727] = 0xC8;

        var text = GpuPowerDiagnosticFormatter.Format(Snapshot(Readout(bytes)) with { PowerOnline = false }).Text;

        Assert.Contains("模式：办公 · 电池供电", text);
        Assert.Contains("注意：0490 bit0 或 0456 bit7 为 0，按已分析的固件此时不下发功耗增量。", text);
        Assert.Contains("注意：固件处于自定义分支，下发值来自 0744 / 0746。", text);
    }

    [Fact]
    public void PartsThatCouldNotBeReadAreNamedAndTheRestKept()
    {
        var snapshot = Snapshot(GpuPowerEcReadout.Unavailable("需要以管理员身份运行"), nvidia: []) with
        {
            NvidiaFailure = "未找到 nvidia-smi（没有 NVIDIA 独显驱动）",
            Nvpcf = NvpcfDevice.Missing,
            Oem = null,
            OemFailure = "读取超时",
            PowerOnline = null
        };

        var report = GpuPowerDiagnosticFormatter.Format(snapshot);

        Assert.Contains("显卡驱动：未找到 nvidia-smi（没有 NVIDIA 独显驱动）", report.Text);
        Assert.Contains("NVPCF：未找到设备（ACPI NVDA0820）", report.Text);
        Assert.Contains("官方接口：读取超时", report.Text);
        Assert.Contains("EC：需要以管理员身份运行", report.Text);
        Assert.Contains("模式：办公 · 供电未知", report.Text);
        Assert.Contains("机型：MECHREVO Model X", report.Text);
        Assert.Equal("没有读到 NVIDIA 驱动的功耗上限。", report.Summary);
    }

    [Fact]
    public void AnUnhealthyNvpcfAndPartialEcReadAreReported()
    {
        var bytes = GpuPowerEcReaderTests.FakeBackend.Office.Where(pair => pair.Key != 0x91C3).ToDictionary();
        var ec = new GpuPowerEcReadout(bytes, [0x91C3], 3, null);

        var text = GpuPowerDiagnosticFormatter.Format(Snapshot(ec) with
        {
            Nvpcf = new NvpcfDevice(true, "NVIDIA Platform Controllers and Framework", "Error", 28)
        }).Text;

        Assert.Contains("NVPCF：异常，设备管理器代码 28（NVIDIA Platform Controllers and Framework，Error）", text);
        Assert.Contains("91C0 32 05 32 ?? 3C 3C CD 00 ← 当前", text);
        Assert.Contains("EC 未读到：91C3", text);
        Assert.Contains("EC 忙时重读：3 次", text);
    }

    [Fact]
    public void NvidiaCsvKeepsTheDriversOwnWordsForMissingFields()
    {
        var readings = NvidiaPowerReading.ParseCsv(
            "NVIDIA GeForce RTX 5060 Laptop GPU, 581.57, 98.07.1A.00.03, P0, 61, 79.12, [N/A], 100.00, 115.00, 5.00, 115.00, 2610, 3090\r\n" +
            "broken, line\r\n" +
            "NVIDIA RTX A1000, 581.57, 96.00, P8, 40, [Not Supported], 60.00, 60.00, 60.00, 30.00, 60.00, 210, 1900\r\n");

        Assert.Equal(2, readings.Count);
        Assert.Equal("N/A", readings[0].RequestedW);
        Assert.Equal("100.00", readings[0].EnforcedW);
        Assert.Equal("Not Supported", readings[1].DrawW);
        Assert.Equal(115, NvidiaPowerReading.Watts(readings[0].MaximumW));
        Assert.Null(NvidiaPowerReading.Watts(readings[0].RequestedW));
    }

    [Fact]
    public async Task TheCollectorKeepsEveryPartWhenOneFails()
    {
        var collector = new GpuPowerDiagnosticCollector("1.0.7-beta.2+20261116",
            _ => ValueTask.FromResult<PlatformModeSelection?>(new PlatformModeSelection(PlatformMode.TurboQuiet) { IsBasicTurbo = false }),
            _ => ValueTask.FromResult(OemPayload),
            readMachine: () => throw new InvalidOperationException("WMI down"),
            readPowerOnline: () => true,
            readNvidia: _ => ValueTask.FromResult<(IReadOnlyList<NvidiaPowerReading>, string?)>(([Rtx5090], null)),
            readNvpcf: () => new NvpcfDevice(true, "NVIDIA Platform Controllers and Framework", "OK", 0),
            readEc: _ => Readout(GpuPowerEcReaderTests.FakeBackend.Office),
            now: () => new DateTimeOffset(2026, 11, 16, 20, 30, 0, TimeSpan.FromHours(8)));

        var report = await collector.CollectAsync(CancellationToken.None);

        Assert.Contains("机型：未读取", report.Text);
        Assert.Contains("模式：狂暴 · 静音 · 接电", report.Text);
        Assert.Contains("官方接口：TGP 95–150（设定 130，开关 1）· 动态加速 5–25（设定 20，开关 1）· OperatingMode 0", report.Text);
        Assert.Contains("91C0 32 05 32 05 3C 3C CD 00 ← 当前", report.Text);
        Assert.Equal("驱动生效上限 150 W，显卡最大 175 W。", report.Summary);
    }

    // nvidia-smi's own words after a failure can include a path; the shared report gets a fixed reason.
    [Fact]
    public async Task TheCollectorDropsWhatNvidiaSmiPrintedOnFailure()
    {
        var collector = new GpuPowerDiagnosticCollector("x", null, null,
            readMachine: () => Machine,
            readPowerOnline: () => true,
            readNvidia: _ => ValueTask.FromResult<(IReadOnlyList<NvidiaPowerReading>, string?)>(([], @"nvidia-smi 退出码 9：Failed to load C:\Users\someone\nvml.dll")),
            readNvpcf: () => NvpcfDevice.Missing,
            readEc: _ => GpuPowerEcReadout.Unavailable("未读取"));

        var text = (await collector.CollectAsync(CancellationToken.None)).Text;

        Assert.Contains("显卡驱动：nvidia-smi 退出码 9", text);
        Assert.DoesNotContain("someone", text);
        Assert.DoesNotContain("nvml", text);
    }

    [Fact]
    public async Task TheCollectorNamesAnOemServiceThatDoesNotAnswer()
    {
        var collector = new GpuPowerDiagnosticCollector("x",
            _ => throw new IOException("GCU closed"),
            _ => throw new IOException("GCU closed"),
            readMachine: () => Machine,
            readPowerOnline: () => null,
            readNvidia: _ => ValueTask.FromResult<(IReadOnlyList<NvidiaPowerReading>, string?)>(([], "nvidia-smi 超时")),
            readNvpcf: () => throw new UnauthorizedAccessException("denied"),
            readEc: _ => GpuPowerEcReadout.Unavailable("EC 接口不可用（WMI NotFound）"));

        var text = (await collector.CollectAsync(CancellationToken.None)).Text;

        Assert.Contains("模式：读取失败 · 供电未知", text);
        Assert.Contains("官方接口：读取失败（IOException）", text);
        Assert.Contains("显卡驱动：nvidia-smi 超时", text);
        Assert.Contains("NVPCF：读取失败（UnauthorizedAccessException）", text);
        Assert.DoesNotContain("GCU closed", text);
        Assert.DoesNotContain("denied", text);
        Assert.Contains("EC：EC 接口不可用（WMI NotFound）", text);
    }

    [Theory]
    [InlineData(PlatformMode.Office, 0, false, "办公")]
    [InlineData(PlatformMode.Balanced, 0, false, "平衡")]
    [InlineData(PlatformMode.TurboQuiet, 0, true, "狂暴")]
    [InlineData(PlatformMode.TurboQuiet, 0, false, "狂暴 · 静音")]
    [InlineData(PlatformMode.TurboHigh, 0, false, "狂暴 · 高能")]
    [InlineData(PlatformMode.Custom, 2, false, "自定义 2")]
    public void ModesAreNamedAsOnTheHomePage(PlatformMode mode, int profile, bool basicTurbo, string expected) =>
        Assert.Equal(expected, GpuPowerDiagnosticCollector.DescribeMode(new PlatformModeSelection(mode, profile) { IsBasicTurbo = basicTurbo }));

    private static GpuPowerEcReadout Readout(IReadOnlyDictionary<ushort, byte> bytes) =>
        new(bytes, GpuPowerEcReader.Addresses.Where(address => !bytes.ContainsKey(address)).ToArray(), 0, null);

    private static GpuPowerDiagnosticSnapshot Snapshot(GpuPowerEcReadout ec, IReadOnlyList<NvidiaPowerReading>? nvidia = null) =>
        new(new DateTimeOffset(2026, 11, 16, 20, 30, 0, TimeSpan.FromHours(8)), "1.0.7-beta.2+20261116", "办公", true, Machine,
            nvidia ?? [Rtx5090], null, new NvpcfDevice(true, "NVIDIA Platform Controllers and Framework", "OK", 0), null,
            new Dictionary<string, string?> { ["ConfigurableTGPMinimum"] = "95", ["ConfigurableTGPMaximum"] = "150" }, null, ec);
}
