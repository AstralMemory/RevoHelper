using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using RevoHelper.Core.Dashboard;

namespace RevoHelper.App.Diagnostics;

/// <summary>
/// Turns what the window knows and what the system read into plain text for the developer. The
/// text stays in Chinese in both UI languages: it is read by the developer, like a log. The brief
/// report always fits in <see cref="BriefLimit"/> characters so it can go into one Bilibili message.
/// </summary>
public static class DiagnosticReportFormatter
{
    public const int BriefLimit = 500;
    public const int RuntimeLogLines = 150;
    public const int RuntimeLineLength = 300;
    private const string NewLine = "\r\n";
    // Words the log uses for a failure, and the structured results that mean one (performance-schedule).
    private static readonly Regex Problem = new(
        @"失败|异常|错误|超时|拒绝|未确认|exception|failed|error|denied|unhandled|result=(?:rollback-needed|unconfirmed|rejected|exception|partial)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // "2026-11-19T21:46:28.6326588+00:00 gcu-connection: connecting: …" → gcu-connection
    private static readonly Regex Area = new(@"^\S+\s+([A-Za-z0-9][A-Za-z0-9._-]{0,40}):",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // Areas that only ever log a failure.
    private static readonly HashSet<string> FailureAreas = new(StringComparer.Ordinal) { "unhandled", "unhandled-ui", "unobserved-task", "gcu-error" };

    public static string Format(DiagnosticAppState app, DiagnosticSystemFacts system, DiagnosticReportDetail detail,
        DateTimeOffset at, DiagnosticPrivacy privacy) =>
        detail == DiagnosticReportDetail.Brief ? Brief(app, system, at, privacy) : Full(app, system, at, privacy);

    public static string Brief(DiagnosticAppState app, DiagnosticSystemFacts system, DateTimeOffset at, DiagnosticPrivacy privacy)
    {
        // Shown in this order. When the text is too long the lowest priority gives way first, to its
        // short form if it has one and then entirely: readings change from second to second, and
        // whether anything failed is what a first look at a bug needs.
        var (features, shortFeatures) = BriefFeatures(app);
        var (problems, shortProblems) = BriefProblems(system);
        var lines = new List<BriefLine>
        {
            new($"RevoHelper 简短诊断 · {app.Version} · {Time(at)}"),
            new("机型：" + Machine(system.Machine, brief: true)),
            new($"系统：Windows {Clip(Or(system.Machine?.Windows, "?"), 24)} · CPU {Clip(Tidy(Or(system.Machine?.Processor, "未知")), 48)}{Cores(app)}"),
            new("显卡：" + BriefGpu(app, system)),
            new($"状态：模式 {Clip(system.Mode, 20)} · {Power(system.PowerOnline)}{Battery(app)} · 限充 {app.ChargeLimit}"),
            new($"官方：控制台{app.OfficialConsoleStateText} · {Clip(Trim(app.OemConnection), 40)}"),
            new(features, 2, shortFeatures),
            new("读数：" + BriefReadings(app.Devices), 1),
            new(problems, 3, shortProblems)
        };

        string Build() => privacy.Clean(string.Join(NewLine, lines.Select(line => line.Text)));
        var text = Build();
        while (text.Length > BriefLimit && lines.Any(line => line.Priority > 0))
        {
            var index = Enumerable.Range(0, lines.Count).Where(position => lines[position].Priority > 0).MinBy(position => lines[position].Priority);
            if (lines[index].Short is { } shorter) lines[index] = lines[index] with { Text = shorter, Short = null };
            else lines.RemoveAt(index);
            text = Build();
        }
        return Clip(text, BriefLimit);
    }

    public static string Full(DiagnosticAppState app, DiagnosticSystemFacts system, DateTimeOffset at, DiagnosticPrivacy privacy)
    {
        var text = new StringBuilder();
        void Line(string line = "") => text.Append(line).Append(NewLine);
        void Section(string title) { Line(); Line($"[{title}]"); }

        Line($"RevoHelper 完整诊断 · {app.Version} · {Time(at)}");
        Line("只包含软硬件型号、读数和运行记录；不含用户名、电脑名、序列号、文件夹路径和网络地址。");

        Section("机器");
        Line("机型：" + Machine(system.Machine, brief: false));
        Line($"处理器：{Or(system.Machine?.Processor, "未知")}{Cores(app)}");
        Line($"Windows：{Or(system.Machine?.Windows, "未知")}");

        Section("运行状态");
        Line($"模式：{system.Mode}（固件回读）· 界面选中 {Or(app.SelectedProfile, "无")}");
        Line($"供电：{Power(system.PowerOnline)}{Battery(app)}");
        Line($"限充：{app.ChargeLimit} · {Or(app.ChargeLimitDetail, "无说明")}");
        Line($"官方控制台：{app.OfficialConsoleStateText}");
        Line($"GCU 控制通道：{Or(app.OemConnection, "未知")}");

        Section("功能检测");
        if (!app.CapabilitiesDetected) Line("尚未完成检测。");
        foreach (var feature in app.Features) Line($"{feature.Name}：{feature.State} · {feature.Reason}");
        foreach (var backend in app.CpuBackends) Line($"CPU 控制 {backend.Name}：{backend.State} · {backend.Reason}");

        Section("设备读数");
        if (app.Devices.Count == 0) Line("没有设备。");
        foreach (var device in app.Devices)
            Line($"{Kind(device.Kind)}：{Or(device.Model, device.Label)} · 占用 {device.Usage} · 温度 {device.Temperature} · " +
                 $"功耗 {device.Power} · 频率 {device.Frequency}{(string.IsNullOrWhiteSpace(device.Details) ? "" : " · " + device.Details)}");

        Section("可能冲突的服务");
        if (app.Services.Count == 0) Line("未检测或没有发现。");
        foreach (var service in app.Services) Line(service);

        Section("显卡驱动");
        if (system.Gpus.Count == 0) Line(system.GpuFailure ?? "没有读到 NVIDIA 显卡。");
        foreach (var gpu in system.Gpus)
            Line($"{gpu.Name} · 驱动 {gpu.Driver} · 功耗上限 生效 {gpu.EnforcedW} / 默认 {gpu.DefaultW} / 最大 {gpu.MaximumW} W");

        Section("显卡功耗（只读 EC）");
        Line(Or(system.GpuPowerReport, "未生成。"));

        Section($"限充日志（最近 {Math.Max(0, system.ChargeLog.Count - 1)} 条）");
        if (system.ChargeLog.Count == 0) Line("没有记录。");
        foreach (var line in system.ChargeLog) Line(line);

        var log = system.RuntimeLog.TakeLast(RuntimeLogLines).ToArray();
        Section($"运行日志（近 24 小时，{LogState(system.RuntimeLogState)}，" +
                $"{(system.RuntimeLog.Count > log.Length ? $"最后 {log.Length} / 共 {system.RuntimeLog.Count}" : $"共 {log.Length}")} 行）");
        if (log.Length == 0) Line("没有记录。");
        // Cleaned before it is cut, so a cut cannot break a path in half and hide it from the check.
        foreach (var line in log) Line(Clip(privacy.Clean(line), RuntimeLineLength));

        return privacy.Clean(text.ToString().TrimEnd());
    }

    /// <summary>Log lines that report a failure, counted by area.</summary>
    public static IReadOnlyList<(string Area, int Count)> Problems(IEnumerable<string> lines)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            try
            {
                var area = Area.Match(line) is { Success: true } match ? match.Groups[1].Value : "其它";
                if (!FailureAreas.Contains(area) && !Problem.IsMatch(line)) continue;
                counts[area] = counts.GetValueOrDefault(area) + 1;
            }
            catch (RegexMatchTimeoutException) { }
        }
        return counts.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (pair.Key, pair.Value)).ToArray();
    }

    // Without NVIDIA's numbers the report still names the GPUs the window found.
    private static string BriefGpu(DiagnosticAppState app, DiagnosticSystemFacts system)
    {
        if (system.Gpus.Count > 0)
        {
            var gpu = system.Gpus[0];
            return $"{Clip(gpu.Name, 48)} · 驱动 {Clip(gpu.Driver, 16)} · 上限 {Clip(gpu.EnforcedW, 8)}/{Clip(gpu.MaximumW, 8)} W" +
                   (system.Gpus.Count > 1 ? $"（另有 {system.Gpus.Count - 1} 块）" : "");
        }
        var known = app.Devices.Where(device => device.Kind != DeviceKind.Cpu)
            .Select(device => $"{Kind(device.Kind)} {Clip(Or(device.Model, device.Label), 40)}").ToArray();
        var reason = Clip(system.GpuFailure ?? "没有读到 NVIDIA 显卡", 40);
        return known.Length == 0 ? reason : $"{string.Join("，", known)}（{reason}）";
    }

    /// <param name="Priority">0 always stays; otherwise the lowest number gives way first.</param>
    /// <param name="Short">Tried before the line is dropped.</param>
    private sealed record BriefLine(string Text, int Priority = 0, string? Short = null);

    private static (string Text, string? Short) BriefFeatures(DiagnosticAppState app)
    {
        if (!app.CapabilitiesDetected) return ("功能：尚未完成检测", null);
        var missing = app.Features.Where(feature => feature.State != "可用").ToArray();
        return missing.Length == 0
            ? ("功能：全部可用", null)
            : ("未确认：" + string.Join("、", missing.Select(feature => $"{feature.Name}（{Clip(Trim(feature.Reason), 16)}）")),
               "未确认：" + string.Join("、", missing.Select(feature => Clip(feature.Name, 12))));
    }

    private static string BriefReadings(IReadOnlyList<DeviceStatus> devices) => devices.Count == 0
        ? "无"
        : string.Join("｜", devices.Select(device =>
            $"{Kind(device.Kind)} {Clip(device.Usage, 10)} {Clip(device.Temperature, 10)} {Clip(device.Power, 10)} {Clip(device.Frequency, 12)}"));

    private static (string Text, string? Short) BriefProblems(DiagnosticSystemFacts system)
    {
        if (system.RuntimeLogState is DiagnosticLogState.Unreadable) return ("运行日志：没能读取", null);
        if (system.RuntimeLogState is DiagnosticLogState.NoFile) return ("运行日志：没有日志文件", null);
        var problems = Problems(system.RuntimeLog);
        var partly = system.RuntimeLogState is DiagnosticLogState.PartlyRead ? "（日志只读到一部分）" : "";
        if (problems.Count == 0) return ($"近 24 小时没有记录到失败{partly}", null);
        var count = $"近 24 小时失败 {problems.Sum(problem => problem.Count)} 条{partly}";
        return (count + "：" + string.Join("，", problems.Take(4).Select(problem => $"{Clip(problem.Area, 24)}×{problem.Count}")), count);
    }

    private static string LogState(DiagnosticLogState state) => state switch
    {
        DiagnosticLogState.Read => "已读取",
        DiagnosticLogState.NoFile => "没有日志文件",
        DiagnosticLogState.PartlyRead => "只读到一部分",
        _ => "没能读取"
    };

    private static string Machine(DiagnosticMachine? machine, bool brief)
    {
        if (machine is null) return "未读取";
        int? limit = brief ? 40 : null;
        var name = Or(string.Join(" ", new[] { machine.Manufacturer, machine.Model }.Where(part => !string.IsNullOrWhiteSpace(part))), "未知");
        var text = Clip(name, limit);
        if (!string.IsNullOrWhiteSpace(machine.Family) && machine.Family != machine.Model) text += $"（{Clip(machine.Family, limit)}）";
        if (!string.IsNullOrWhiteSpace(machine.Baseboard)) text += " · 主板 " + Clip(machine.Baseboard, limit);
        text += " · BIOS " + Clip(Or(machine.Bios, "未知"), limit);
        if (!string.IsNullOrWhiteSpace(machine.EmbeddedController)) text += " · EC " + Clip(machine.EmbeddedController, limit);
        return text;
    }

    public static string Kind(DeviceKind kind) => kind switch
    {
        DeviceKind.Cpu => "CPU",
        DeviceKind.IntegratedGpu => "核显",
        DeviceKind.DiscreteGpu => "独显",
        _ => "显卡"
    };

    private static string Cores(DiagnosticAppState app) =>
        app.CpuCores > 0 ? $"（{app.CpuCores} 核 {app.CpuThreads} 线程）" : "";

    private static string Battery(DiagnosticAppState app) =>
        app.BatteryPercent is { } percent ? $" · 电量 {percent.ToString(CultureInfo.InvariantCulture)}%" : "";

    private static string Power(bool? online) => online switch { true => "接电", false => "电池供电", _ => "供电未知" };

    private static string Time(DateTimeOffset at) => at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string Trim(string text) => text.Trim().TrimEnd('。', '.');

    // "Intel(R) Core(TM) Ultra 9 275HX" → "Intel Core Ultra 9 275HX": the marks cost characters in the brief report.
    private static string Tidy(string processor) =>
        string.Join(' ', processor.Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    /// <summary>Cuts to at most <paramref name="limit"/> characters with an ellipsis, never between the two halves of a surrogate pair.</summary>
    public static string Clip(string text, int? limit)
    {
        if (limit is not { } max || text.Length <= max) return text;
        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }
}
