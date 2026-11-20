using System.Globalization;
using System.Text;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.GpuPower;

/// <summary>Everything the report shows. A null part was not read; its failure says why.</summary>
public sealed record GpuPowerDiagnosticSnapshot(
    DateTimeOffset At,
    string AppVersion,
    string Mode,
    bool? PowerOnline,
    GpuDiagnosticMachine? Machine,
    IReadOnlyList<NvidiaPowerReading> Nvidia,
    string? NvidiaFailure,
    NvpcfDevice? Nvpcf,
    string? NvpcfFailure,
    IReadOnlyDictionary<string, string?>? Oem,
    string? OemFailure,
    GpuPowerEcReadout Ec);

/// <summary>
/// Plain text for the developer. Raw values come first and stay complete; the few derived lines
/// follow the YAOSHI (MRO24) firmware study and are marked as estimates, because other models'
/// firmware may lay the table out differently. That difference is what these reports are for.
/// </summary>
public static class GpuPowerDiagnosticFormatter
{
    public static IReadOnlyList<string> OemFields { get; } = Array.AsReadOnly(
    [
        "OperatingMode", "ConfigurableTGPSwitch", "ConfigurableTGPTarget", "ConfigurableTGPMinimum", "ConfigurableTGPMaximum",
        "DynamicBoostSwitch", "DynamicBoost", "DynamicBoostMinimum", "DynamicBoostMaximum"
    ]);

    private const string NewLine = "\r\n";

    public static GpuPowerDiagnosticReport Format(GpuPowerDiagnosticSnapshot snapshot)
    {
        var text = new StringBuilder();
        void Line(string line) => text.Append(line).Append(NewLine);

        Line($"RevoHelper 显卡功耗诊断 · {snapshot.AppVersion} · {snapshot.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        if (snapshot.Machine is { } machine)
        {
            var name = Join(" ", machine.Manufacturer, machine.Model);
            var detail = Join("，", machine.Family, machine.Baseboard.Length > 0 ? "主板 " + machine.Baseboard : "");
            Line($"机型：{Or(name, "未知")}{(detail.Length > 0 ? $"（{detail}）" : "")} · BIOS {Or(machine.Bios, "未知")}" +
                 (machine.EmbeddedController.Length > 0 ? $" · EC {machine.EmbeddedController}" : ""));
            Line($"处理器：{Or(machine.Processor, "未知")} · Windows {machine.Windows}");
        }
        else Line("机型：未读取");
        Line($"模式：{snapshot.Mode} · {snapshot.PowerOnline switch { true => "接电", false => "电池供电", _ => "供电未知" }}");

        if (snapshot.Nvidia.Count == 0) Line($"显卡驱动：{snapshot.NvidiaFailure ?? "没有读到 NVIDIA 显卡"}");
        for (var index = 0; index < snapshot.Nvidia.Count; index++)
        {
            var gpu = snapshot.Nvidia[index];
            Line($"{(snapshot.Nvidia.Count > 1 ? $"显卡 {index + 1}" : "显卡")}：{gpu.Name} · 驱动 {gpu.Driver} · VBIOS {gpu.Vbios}");
            Line($"驱动功耗上限（W）：生效 {W(gpu.EnforcedW)} · 设定 {W(gpu.RequestedW)} · 默认 {W(gpu.DefaultW)} · " +
                 $"最小 {W(gpu.MinimumW)} · 最大 {W(gpu.MaximumW)} · 当前功耗 {W(gpu.DrawW)}");
            Line($"显卡状态：{gpu.PState} · {gpu.TemperatureC} °C · 核心 {gpu.ClockMhz} / {gpu.MaximumClockMhz} MHz");
        }

        Line(snapshot.Nvpcf switch
        {
            null => $"NVPCF：{snapshot.NvpcfFailure ?? "未读取"}",
            { Present: false } => "NVPCF：未找到设备（ACPI NVDA0820）",
            { ErrorCode: 0 } nvpcf => $"NVPCF：正常（{nvpcf.Name}）",
            var nvpcf => $"NVPCF：异常，设备管理器代码 {nvpcf.ErrorCode?.ToString(CultureInfo.InvariantCulture) ?? "?"}" +
                         $"（{nvpcf.Name}，{nvpcf.Status}）"
        });

        if (snapshot.Oem is { } oem && oem.Values.Any(value => value is not null))
        {
            string V(string field) => oem.TryGetValue(field, out var value) && value is not null ? value : "?";
            Line($"官方接口：TGP {V("ConfigurableTGPMinimum")}–{V("ConfigurableTGPMaximum")}（设定 {V("ConfigurableTGPTarget")}，" +
                 $"开关 {V("ConfigurableTGPSwitch")}）· 动态加速 {V("DynamicBoostMinimum")}–{V("DynamicBoostMaximum")}" +
                 $"（设定 {V("DynamicBoost")}，开关 {V("DynamicBoostSwitch")}）· OperatingMode {V("OperatingMode")}");
        }
        else Line($"官方接口：{snapshot.OemFailure ?? "没有显卡功耗字段"}");

        AppendEc(snapshot, Line);
        return new(text.ToString().TrimEnd(), Summary(snapshot));
    }

    public static string Summary(GpuPowerDiagnosticSnapshot snapshot) =>
        snapshot.Nvidia.FirstOrDefault() is { } gpu &&
        NvidiaPowerReading.Watts(gpu.EnforcedW) is { } enforced && NvidiaPowerReading.Watts(gpu.MaximumW) is { } maximum
            ? $"驱动生效上限 {Number(enforced)} W，显卡最大 {Number(maximum)} W。"
            : "没有读到 NVIDIA 驱动的功耗上限。";

    /// <summary>The row the firmware uses now, by the MRO24 rule: 07C8 bit 5 picks the 91C0 table, 0A6E the row.</summary>
    public static ushort? ActiveRow(GpuPowerEcReadout ec) =>
        ec[0x07C8] is { } select && ec[0x0A6E] is { } offset && offset is 0x00 or 0x10 or 0x20
            ? (ushort)(((select & 0x20) != 0 ? 0x91C0 : 0x9190) + offset)
            : null;

    private static void AppendEc(GpuPowerDiagnosticSnapshot snapshot, Action<string> line)
    {
        var ec = snapshot.Ec;
        if (ec.Bytes.Count == 0)
        {
            line($"EC：{ec.Failure ?? "未读取"}");
            return;
        }
        line("EC 门控：" + Hex(ec, 0x0490, 0x0456, 0x0741, 0x0727, 0x072B, 0x07C4, 0x07C7, 0x07C8, 0x0A6E, 0x07D2));
        line("EC 自定义：" + Hex(ec, 0x0743, 0x0744, 0x0745, 0x0746, 0x0747));
        line("EC 下发/镜像：" + string.Join(" ", Pair(ec, 0x09EA, 0x0788), Pair(ec, 0x09EB, 0x07D4), Pair(ec, 0x09EC, 0x07D5),
            Pair(ec, 0x07F7, 0x9062), Hex(ec, 0x0763, 0x0764, 0x07D6, 0x07D7)));
        var active = ActiveRow(ec);
        line("EC 表（每行前 8 字节）：");
        for (var row = 0; row < GpuPowerEcReader.TableRows; row++)
        {
            var start = (ushort)(GpuPowerEcReader.TableStart + row * GpuPowerEcReader.RowStride);
            var bytes = string.Join(" ", Enumerable.Range(0, GpuPowerEcReader.RowBytes).Select(column => Byte(ec, (ushort)(start + column))));
            line($"{start:X4} {bytes}{(active == start ? " ← 当前" : "")}");
        }
        if (ec.Failure is not null) line($"EC：{ec.Failure}（已读 {ec.Bytes.Count} 项）");
        else if (ec.Unread.Count > 0)
            line($"EC 未读到：{string.Join(" ", ec.Unread.Take(8).Select(address => address.ToString("X4")))}" +
                 (ec.Unread.Count > 8 ? $" 等 {ec.Unread.Count} 项" : ""));
        if (ec.BusyRetries > 0) line($"EC 忙时重读：{ec.BusyRetries} 次");

        line(active is { } current
            ? $"当前行（推断）：{current:X4}（07C8 bit5={(ec[0x07C8]!.Value >> 5) & 1}，0A6E={ec[0x0A6E]!.Value:X2}）"
            : $"当前行（推断）：无法确定（07C8={Byte(ec, 0x07C8)}，0A6E={Byte(ec, 0x0A6E)}）");
        if (ec[0x09EA] is not { } baseIncrease || ec[0x09EC] is not { } boost) return;
        var agreement = active is { } rowStart && ec[rowStart] is { } rowBase && ec[(ushort)(rowStart + 1)] is { } rowBoost
            ? rowBase == baseIncrease && rowBoost == boost ? "，与当前行一致" : $"，当前行为 +{rowBase} / +{rowBoost} W"
            : "";
        line($"固件下发：基础 +{baseIncrease} W，动态加速 +{boost} W{agreement}");
        if (snapshot.Nvidia.FirstOrDefault() is { } gpu && NvidiaPowerReading.Watts(gpu.DefaultW) is { } defaultWatts)
        {
            var maximum = NvidiaPowerReading.Watts(gpu.MaximumW);
            line($"推算本档上限：默认 {Number(defaultWatts)} + {baseIncrease} + {boost} = {Number(defaultWatts + baseIncrease + boost)} W" +
                 (maximum is { } max ? $"（显卡最大 {Number(max)} W）" : ""));
        }
        if (ec[0x0490] is { } power && (power & 1) == 0 || ec[0x0456] is { } gpuState && (gpuState & 0x80) == 0)
            line("注意：0490 bit0 或 0456 bit7 为 0，按已分析的固件此时不下发功耗增量。");
        if (ec[0x0741] is { } custom && (custom & 1) != 0 && ec[0x0727] is { } overclock && (overclock & 0x40) != 0)
            line("注意：固件处于自定义分支，下发值来自 0744 / 0746。");
    }

    private static string Hex(GpuPowerEcReadout ec, params ushort[] addresses) =>
        string.Join(" ", addresses.Select(address => $"{address:X4}={Byte(ec, address)}"));

    private static string Pair(GpuPowerEcReadout ec, ushort source, ushort mirror) =>
        $"{source:X4}={Byte(ec, source)}/{mirror:X4}={Byte(ec, mirror)}";

    private static string Byte(GpuPowerEcReadout ec, ushort address) => ec[address] is { } value ? value.ToString("X2") : "??";

    // "175.00" reads as 175; words such as N/A stay as the driver wrote them.
    private static string W(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var watts) && double.IsFinite(watts)
            ? Number(watts) : raw;

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Join(string separator, params string[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
