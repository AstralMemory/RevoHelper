using System.Text.RegularExpressions;

namespace RevoHelper.App.Presentation;

/// <summary>Exact UI copy plus bounded, anchored templates; unknown native errors and user names stay untouched.</summary>
public static class UiTextCatalog
{
    private static readonly IReadOnlyDictionary<string, string> English = BuildCatalog();
    public static bool HasTranslation(string text) => English.ContainsKey(text);
    public static string Translate(string text, string? language)
    {
        if (LocalizedUiCopy.NormalizeLanguageCode(language) != "en-US" || string.IsNullOrEmpty(text)) return text;
        if (English.TryGetValue(text, out var translated)) return translated;
        if (text.Length > 8192 || !text.Any(character => character is >= '\u4e00' and <= '\u9fff')) return text;
        foreach (var (pattern, format) in Templates)
        {
            try
            {
                var match = pattern.Match(text);
                if (match.Success) return format(match);
            }
            catch (RegexMatchTimeoutException) { return text; }
        }
        return text;
    }

    // A lighting zone, switch or toggle label followed by one of these fixed endings.
    private static readonly IReadOnlyDictionary<string, string> ZoneEndings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["已开启并回读确认。"] = " turned on and verified.",
        ["已关闭并回读确认。"] = " turned off and verified.",
        ["电源写入后未得到目标状态回读。"] = ": power was sent, but the readback did not confirm it.",
        ["未返回当前配置，无法建立可见的开启状态。"] = " did not report its settings, so it could not be turned on visibly.",
        ["灯效已发送，但最终开启状态未得到回读确认。"] = ": effect sent, but the readback did not confirm that it is on.",
        ["命令已发送；仅确认电源已开启，灯效与颜色未回报，请观察实体灯。"] = ": command sent; only power on was confirmed. Effect and color are not reported, so check the light itself.",
        ["命令已发送；仅确认电源已关闭，灯效与颜色未回报，请观察实体灯。"] = ": command sent; only power off was confirmed. Effect and color are not reported, so check the light itself.",
        ["灯效已应用并回读确认。"] = ": effect applied and verified.",
        ["命令已发送，但回读未匹配目标状态，未确认生效。"] = ": command sent, but the readback did not match; not confirmed.",
        ["已保留最新选择，等待应用。"] = ": keeping your latest choice until it applies.",
        ["已保留最新选择，等待应用；上一条反馈不会覆盖编辑值。"] = ": keeping your latest choice until it applies; earlier feedback will not replace it."
    };

    private static readonly IReadOnlyDictionary<string, string> LabelEndings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["已开启并回读确认。"] = " turned on and verified.",
        ["已关闭并回读确认。"] = " turned off and verified.",
        ["已启用并回读确认。"] = " enabled and verified.",
        ["已禁用并回读确认。"] = " disabled and verified.",
        ["写入后未得到目标状态回读。"] = " change was sent, but the readback did not confirm it.",
        ["命令已发送，但设备状态接口未返回该项，无法确认实际状态。"] = ": command sent, but the device status did not include it; the actual state is unconfirmed.",
        ["命令已发送，但 OEM 状态未返回该项，无法确认实际状态。"] = ": command sent, but the OEM status did not include it; the actual state is unconfirmed."
    };

    private const string Zone = "(键盘背光|海岸灯带|LOGO 灯|水冷灯)";

    private static string Alternation(IEnumerable<string> endings) =>
        string.Join("|", endings.OrderByDescending(ending => ending.Length).Select(Regex.Escape));

    private static readonly (Regex Pattern, Func<Match, string> Format)[] Templates =
    [
        Rule(@"^电源暂停后的恢复未确认；不会自动重启限充。([\s\S]*)$", m => "Restoration after the power pause is not confirmed; charge limiting will not restart automatically. " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^限充自动恢复未确认，保持关闭且不自动重试。([\s\S]*)$", m => "Automatic charge-limit resumption is not confirmed; remaining off without automatic retries. " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^限充已安全停止，不自动重启。([\s\S]*)$", m => "Charge limiting stopped safely and will not restart automatically. " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^(限充操作失败|停止限充失败|恢复限充失败)：(.+)$", m => (m.Groups[1].Value switch
        {
            "限充操作失败" => "Charge-limit operation failed",
            "停止限充失败" => "Stopping charge limiting failed",
            _ => "Restoring charge limiting failed"
        }) + ": " + TranslateKnown(m.Groups[2].Value)),
        Rule(@"^(启用|停止|恢复)实验性限充失败：(.+)$", m => (m.Groups[1].Value switch { "启用" => "Enabling", "停止" => "Stopping", _ => "Restoring" }) + " experimental charge limiting failed: " + Translate(m.Groups[2].Value, "en-US")),
        Rule(@"^实验性限充未完成：(.+)$", m => "Experimental charge limiting did not complete: " + Translate(m.Groups[1].Value, "en-US")),
        Rule(@"^实验已停止，监护正在复核恢复：(.+)$", m => "The experiment stopped; the guardian is checking restoration: " + Translate(m.Groups[1].Value, "en-US")),
        Rule(@"^限充未启用：(.+)$", m => "Charge limit not enabled: " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^限充已停止，已恢复原值。(.*)$", m => "Charge limit stopped; original values restored. " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^限充已停止，恢复未确认；固件会在约 30 分钟内自行恢复正常充电。(.*)$", m => "Charge limit stopped; restoration unconfirmed. The firmware returns to normal charging by itself within about 30 minutes. " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^固件的保持计时已在使用（0x([0-9A-F]{4})）；不接管。$", m => "The firmware hold countdown is already in use (0x" + m.Groups[1].Value + "); it will not be taken over."),
        Rule(@"^固件当前不执行保持功能（(.+)）。$", m => "The firmware is not running its hold feature right now (" + m.Groups[1].Value + ")."),
        Rule(@"^原始状态0x([0-9A-F]{4})已改变（当前0x([0-9A-F]{2})）；不接管已有设置。$", m => "Original state 0x" + m.Groups[1].Value + " changed (now 0x" + m.Groups[2].Value + "); existing settings will not be taken over."),
        Rule(@"^部分状态读取失败，已保留已确认状态：(.+)$", m => "Some statuses could not be read; confirmed values were preserved: " + TranslateStatusDetails(m.Groups[1].Value)),
        Rule(@"^部分状态未读取：(.+)$", m => "Some statuses were not read: " + TranslateStatusDetails(m.Groups[1].Value)),
        Rule(@"^(按键与设备状态|电源与电池状态|按键状态|设备开关状态|键盘灯光状态|电池保护状态|电源设置状态|快捷设置状态|充电上限状态)读取失败：(.+)$", m => TranslateKnown(m.Groups[1].Value) + " could not be read: " + TranslateStatusDetails(m.Groups[2].Value)),
        Rule(@"^(按键状态|设备开关状态|键盘灯光状态|电池保护状态|电源设置状态|快捷设置状态|充电上限状态)未返回可识别状态。$", m => TranslateKnown(m.Groups[1].Value) + " did not return a recognized status."),
        Rule(@"^未运行：(.+)\n启动后会自动匹配该程序。$", m => $"Not running: {m.Groups[1].Value}\nIt will be matched when it starts."),
        Rule(@"^当前：([^\n]+)\n([\s\S]+)$", m => $"Current: {m.Groups[1].Value}\n{TranslateKnown(m.Groups[2].Value)}"),
        Rule(@"^已设置为(.+)，尚未生效 · 请点“重启”，关机再开机可能不生效$", m => $"Set to {TranslateKnown(m.Groups[1].Value)}, not active yet · Choose Restart; shutting down and powering on may not apply it"),
        Rule(@"^已写入并回读验证：(.+)；请点“重启”生效，关机再开机可能不生效。$", m => $"Saved and verified: {TranslateKnown(m.Groups[1].Value)}. Choose Restart to apply it; shutting down and powering on may not."),
        Rule(@"^当前：(.+) · 更改后需要重启生效$", m => $"Current: {TranslateKnown(m.Groups[1].Value)} · Restart after changing"),
        Rule(@"^当前：(.+)$", m => "Current: " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^状态：(.+)$", m => "Status: " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^添加：(.+)$", m => "Added: " + m.Groups[1].Value),
        Rule(@"^上次选择：(.+)$", m => "Last selected: " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^(\d+) 秒$", m => m.Groups[1].Value + " s"),
        Rule(@"^(\d+) 分钟$", m => m.Groups[1].Value + " min"),
        Rule(@"^(\d+) 小时$", m => m.Groups[1].Value + " h"),
        Rule(@"^(\d+) 核 · (\d+) 线程$", m => $"{m.Groups[1].Value} cores · {m.Groups[2].Value} threads"),
        Rule(@"^([\d.]+) (GB|MB) 显存$", m => $"{m.Groups[1].Value} {m.Groups[2].Value} VRAM"),
        Rule(@"^固件：(.+) · 仪表(正常|异常)$", m => $"Firmware: {m.Groups[1].Value} · Meter {(m.Groups[2].Value == "正常" ? "normal" : "fault")}"),
        Rule(@"^(CPU|GPU) 曲线最高温度：(\d+)℃$", m => $"{m.Groups[1].Value} curve maximum: {m.Groups[2].Value} °C"),
        Rule(@"^(.+) · 风扇曲线仅供参考$", m => $"{TranslateKnown(m.Groups[1].Value)} · Reference fan curve"),
        Rule(@"^已载入 (.+)；确认参数后点击应用并回读。$", m => $"Loaded {m.Groups[1].Value}. Check the settings, then apply."),
        Rule(@"^已导出 (.+)。$", m => $"Exported {m.Groups[1].Value}."),
        Rule(@"^已恢复 (.+)；打开灯光页后可显式应用。$", m => $"Restored {m.Groups[1].Value}. Open Lighting to apply it."),
        Rule(@"^观察窗口：(.+) / 600 秒 · 空闲 (.+)%$", m => $"Observation: {m.Groups[1].Value} / 600 s · Idle {m.Groups[2].Value}%"),
        Rule(@"^已保存 (.+)%；当前屏幕空间不足，暂按 (.+)% 显示。$", m => $"Saved {m.Groups[1].Value}%; using {m.Groups[2].Value}% to fit this screen."),
        Rule(@"^自定义模式 (\d+)$", m => "Custom " + m.Groups[1].Value),
        Rule(@"^只读传感器 · 更新于 (.+)$", m => "Read-only sensors · Updated " + m.Groups[1].Value),
        Rule(@"^自定义方案 (\d+) 已切换 · 当前仅本地编辑$", m => $"Custom preset {m.Groups[1].Value} selected · Local draft only"),
        Rule(@"^已只读加载 (\d+) 个 OEM 功耗档案$", m => $"Read {m.Groups[1].Value} OEM power profiles"),
        Rule(@"^(键盘背光|海岸灯带|LOGO 灯|水冷灯)：调整后自动应用，仅控制当前分区。$", m => TranslateKnown(m.Groups[1].Value) + ": Changes apply to this zone only."),
        Rule(@"^(键盘背光|海岸灯带|LOGO 灯|水冷灯)已通过 GCU 状态事件同步。$", m => TranslateKnown(m.Groups[1].Value) + " synchronized through GCU."),
        Rule(@"^(键盘背光|海岸灯带|LOGO 灯|水冷灯)灯效已确认；([亮度速方向颜色、]+)未回读确认，请以实际灯光为准。$", m =>
            $"{TranslateKnown(m.Groups[1].Value)} effect confirmed. {m.Groups[2].Value.Replace("亮度", "brightness").Replace("速度", "speed").Replace("方向", "direction").Replace("颜色", "color").Replace("、", ", ")} not confirmed by readback; check the actual light."),
        Rule(@"^已发现 (\d+) 个灯光分区；调整后自动应用。$", m => $"Found {m.Groups[1].Value} lighting zones. Changes apply automatically."),
        Rule(@"^发现 (\d+) 个兼容水冷设备，请选择后连接。$", m => $"Found {m.Groups[1].Value} compatible coolers. Select one to connect."),
        Rule(@"^GCU 配置回读 · PL1 (.+) W · PL2 (.+) W · Boost (.+)$", m => $"GCU readback · PL1 {m.Groups[1].Value} W · PL2 {m.Groups[2].Value} W · Boost {m.Groups[3].Value}"),
        Rule(@"^GCU 配置回读 · (.+) · PL1 (.+) W（(启用|未启用)） · PL2 (.+) W（(启用|未启用)）$", m => $"GCU readback · {m.Groups[1].Value} · PL1 {m.Groups[2].Value} W ({TranslateKnown(m.Groups[3].Value)}) · PL2 {m.Groups[4].Value} W ({TranslateKnown(m.Groups[5].Value)})"),
        Rule(@"^OEM 固件预设 · GCU 配置回显 PL1 (.+) W（(启用|未启用)） / PL2 (.+) W（(启用|未启用)）· 非实际硬件 PL$", m => $"OEM preset · Reported PL1 {m.Groups[1].Value} W ({TranslateKnown(m.Groups[2].Value)}) / PL2 {m.Groups[3].Value} W ({TranslateKnown(m.Groups[4].Value)}) · Not measured hardware limits"),
        Rule(@"^频率回读 · C0 (.+) · C1 (.+) · C2 (.+) MHz$", m => $"Frequency readback · C0 {m.Groups[1].Value} · C1 {m.Groups[2].Value} · C2 {m.Groups[3].Value} MHz"),
        Rule(@"^已应用并回读 · PL1 (.+) W / PL2 (.+) W · (.+)$", m => $"Applied and verified · PL1 {m.Groups[1].Value} W / PL2 {m.Groups[2].Value} W · {m.Groups[3].Value}"),
        Rule(@"^已应用并回读 Boost · (.+)；OEM 预设 PL 未改动$", m => $"Boost applied and verified · {m.Groups[1].Value}; OEM power limits unchanged"),
        Rule(@"^GPU 频率偏移已回读确认：核心 (.+) MHz，显存 (.+) MHz。$", m => $"GPU offsets verified: core {m.Groups[1].Value} MHz, memory {m.Groups[2].Value} MHz."),
        Rule(@"^可尝试范围：核心 (.+)～(.+) MHz；显存 (.+)～(.+) MHz。(.*)$", m => $"Available offsets: core {m.Groups[1].Value} to {m.Groups[2].Value} MHz; memory {m.Groups[3].Value} to {m.Groups[4].Value} MHz. {TranslateKnown(m.Groups[5].Value)}"),
        Rule(@"^(.+) · 高级参数只读$", m => TranslateKnown(m.Groups[1].Value) + " · Advanced settings (read-only)"),
        // V1.0.5: runtime status lines of the lighting, power, display and cooler pages.
        Rule($@"^{Zone}({Alternation(ZoneEndings.Keys)})$", m => TranslateKnown(m.Groups[1].Value) + ZoneEndings[m.Groups[2].Value]),
        Rule($@"^{Zone}当前已关闭；颜色 (#[0-9A-Fa-f]{{6}}) 与灯效参数已保存，打开后自动应用。$", m => $"{TranslateKnown(m.Groups[1].Value)} is off; color {m.Groups[2].Value} and effect settings are saved and apply when it turns on."),
        Rule($@"^{Zone}已选择 (#[0-9A-Fa-f]{{6}})；等待自动应用。$", m => $"{TranslateKnown(m.Groups[1].Value)}: {m.Groups[2].Value} selected; applying automatically."),
        Rule($@"^{Zone}已保存 (#[0-9A-Fa-f]{{6}})；打开后自动应用。$", m => $"{TranslateKnown(m.Groups[1].Value)}: {m.Groups[2].Value} saved; it applies when the zone turns on."),
        Rule($@"^未从官方状态主题发现{Zone}，未发送命令。$", m => $"{TranslateKnown(m.Groups[1].Value)} was not found in the OEM status; no command was sent."),
        Rule($@"^当前{Zone}不支持 (\w+) 灯效。$", m => $"{TranslateKnown(m.Groups[1].Value)} does not support the {m.Groups[2].Value} effect."),
        Rule($@"^切换{Zone}失败：([\s\S]+)$", m => $"Switching {TranslateKnown(m.Groups[1].Value)} failed: {Translate(m.Groups[2].Value, "en-US")}"),
        Rule(@"^(.+)键盘方案已应用并回读。$", m => $"{TranslateKnown(m.Groups[1].Value)} keyboard preset applied and verified."),
        Rule(@"^(.+)没有可应用的已发现分区。$", m => $"{TranslateKnown(m.Groups[1].Value)}: no discovered zone can use it."),
        Rule(@"^键盘与灯带自动熄灯档位 (.+) 已回读确认。$", m => $"Keyboard and light-bar sleep timer set to {Translate(m.Groups[1].Value, "en-US")} and verified."),
        Rule(@"^休眠命令已发送，但官方状态仍为 (\d+)，未确认生效。$", m => $"Sleep timer sent, but the OEM status still reads {m.Groups[1].Value}; not confirmed."),
        Rule(@"^电源与电池状态已读取 · 当前电量 (\d+)%$", m => $"Power and battery status read · Battery {m.Groups[1].Value}%"),
        Rule(@"^深度睡眠已开启（(\d+) 分钟）并回读确认。$", m => $"Deep sleep on ({m.Groups[1].Value} min) and verified."),
        Rule(@"^充电上限已设为 (\d+)% 并从 OEM 状态回读确认。$", m => $"Charge limit set to {m.Groups[1].Value}% and verified through OEM status."),
        Rule(@"^充电上限写入后回读为 (\d+)%，未达到 (\d+)%。$", m => $"The charge limit reads {m.Groups[1].Value}% after writing, not {m.Groups[2].Value}%."),
        Rule(@"^色彩管理已开启，(.+) 色域已回读确认。$", m => $"Color management on; {TranslateKnown(m.Groups[1].Value)} preset verified."),
        Rule(@"^色彩管理与 (.+) 色域命令已提交，官方 ICC 校准流程仍在处理。$", m => $"Color management and {TranslateKnown(m.Groups[1].Value)} preset sent; the OEM ICC calibration is still running."),
        Rule(@"^色彩管理应用失败：(.+)。$", m => $"Color management failed: {m.Groups[1].Value}."),
        Rule(@"^(.+) 色域已应用并回读确认。$", m => $"{TranslateKnown(m.Groups[1].Value)} color preset applied and verified."),
        Rule(@"^(.+) 色域命令已提交，官方 ICC 校准流程仍在处理。$", m => $"{TranslateKnown(m.Groups[1].Value)} color preset sent; the OEM ICC calibration is still running."),
        Rule(@"^(.+) 色域应用失败：(.+)。$", m => $"{TranslateKnown(m.Groups[1].Value)} color preset failed: {m.Groups[2].Value}."),
        Rule(@"^正在写入并回读：(.+)$", m => "Applying and verifying: " + TranslateKnown(m.Groups[1].Value)),
        Rule(@"^当前已经是 (\d+) Hz。$", m => $"Already at {m.Groups[1].Value} Hz."),
        Rule(@"^当前显示路径未提供 (\d+) Hz，未更改显示设置。$", m => $"This display path does not offer {m.Groups[1].Value} Hz; display settings were not changed."),
        Rule(@"^当前机型未确认支持(.+)，没有写入硬件。$", m => $"{TranslateKnown(m.Groups[1].Value)} is not confirmed for this model; nothing was written."),
        Rule(@"^当前已经是(.+)。$", m => $"{TranslateKnown(m.Groups[1].Value)} is already active."),
        Rule(@"^已应用并回读：PL1 (.+) W / PL2 (.+) W。$", m => $"Applied and verified: PL1 {m.Groups[1].Value} W / PL2 {m.Groups[2].Value} W."),
        Rule(@"^(设置定时关机失败|取消定时关机失败)（退出码 (-?\d+)）([\s\S]*)$", m => $"{TranslateKnown(m.Groups[1].Value)} (exit code {m.Groups[2].Value}){m.Groups[3].Value}"),
        Rule(@"^([\s\S]+) 部分状态未读取：([\s\S]+)$", m => Translate(m.Groups[1].Value, "en-US") + " Some statuses were not read: " + TranslateStatusDetails(m.Groups[2].Value)),
        Rule($@"^(.+)({Alternation(LabelEndings.Keys)})$", m => TranslateKnown(m.Groups[1].Value) + LabelEndings[m.Groups[2].Value]),
        // Last: a known prefix before "：" or " · ", followed by a native detail that stays as reported.
        Rule(@"^[\s\S]+$", m => TranslatePrefixed(m.Value))
    ];

    private static string TranslatePrefixed(string text)
    {
        foreach (var separator in new[] { "：", " · " })
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index is < 2 or > 60 || !English.TryGetValue(text[..index], out var prefix)) continue;
            return prefix + (separator == "：" ? ": " : " · ") + Translate(text[(index + separator.Length)..], "en-US");
        }
        return text;
    }

    private static string TranslateKnown(string text) => English.TryGetValue(text, out var translated) ? translated : text;
    private static string TranslateStatusDetails(string text) =>
        string.Join("; ", text.Split('；').Select(part => Translate(part, "en-US")));

    private static (Regex, Func<Match, string>) Rule(string pattern, Func<Match, string> format) =>
        (new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(100)), format);

    private static IReadOnlyDictionary<string, string> BuildCatalog()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in Copy.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.TrimEnd('\r');
            var separator = line.IndexOf('\t');
            if (separator > 0) result.Add(line[..separator], line[(separator + 1)..]);
        }
        return result;
    }

    private const string Copy = """
请先确认原值恢复，不接管待恢复的限充会话。	Confirm restoration of the original values first; a pending charge-limit session will not be taken over.
限充仍在恢复，请等待完成后再选择档位。	Charge limiting is still restoring; wait until it finishes before selecting a limit.
请接通电源后手动启用限充；未启用的会话不会自动开始。	Connect AC before enabling charge limiting manually; a session that has not been enabled will not start automatically.
限充启用未确认，保持关闭；不会自动重试，请检查恢复状态。	Charge-limit enabling is not confirmed; remaining off without automatic retries. Check restoration status.
启用前电源状态已变化；未启用新的限充会话。	Power state changed before enabling; no new charge-limit session was enabled.
底层未确认目标限充会话已启用。	The underlying controller did not confirm that the requested charge-limit session was enabled.
已请求关闭限充，正在确认原值恢复。	Charge-limit shutdown was requested; confirming restoration of the original values.
电源已断开，限充未启用。	AC is disconnected; charge limiting is not enabled.
无法核对电源状态，保持关闭；不会自动重启限充。	Power state could not be checked; remaining off. Charge limiting will not restart automatically.
旧限充会话恢复尚未确认，不并行启用新会话。	Restoration of the previous charge-limit session is not confirmed; no new session will be enabled in parallel.
限充开关保持开启；正在安全暂停并恢复原值，完成后等待电源，不代表正在限制充电。	The charge switch remains on; safely pausing and restoring the original values, then waiting for power. Charging is not necessarily being limited.
限充开关保持开启；已安全暂停，等待电源稳定后恢复，并非正在限制充电。	The charge switch remains on; safely paused, waiting for stable power before resuming. Charging is not currently being limited.
限充开关保持开启；已安全暂停，等待重新接通电源，并非正在限制充电。	The charge switch remains on; safely paused, waiting for AC to reconnect. Charging is not currently being limited.
程序退出，限充已关闭并请求恢复原值。	The app is exiting; charge limiting is off and restoration of the original values was requested.
供电状态处理未完成，停止限充并保留恢复记录。	Power-state handling did not complete; stop charge limiting and retain the recovery record.
限充未连接；默认关闭。	Charge limiting is not connected; off by default.
正在应用限充，尚未确认生效。	Applying charge limiting; effectiveness has not been confirmed.
真正退出程序，独立监护继续恢复限充原值。	Actually exiting the app; the independent guardian continues restoring the original charge-limit state.
已开启：100% 不限制，按原生策略充电；未保持平台覆盖。	Enabled: 100% sets no charge limit and follows the native charging policy; no platform override is held.
限充已关闭；100% 透传模式没有平台覆盖需要恢复。	Charge limiting is off; 100% pass-through has no platform override to restore.
正在停止限充并恢复原值。	Stopping charge limiting and restoring the original values.
恢复未确认；请先使用恢复按钮，不继续启用或换档。	Restoration is not confirmed. Use Restore first; do not enable or change the limit.
限充已关闭，已有实验限制已恢复；按原生策略充电。	Charge limiting is off and the previous experimental limit has been restored; charging follows the native policy.
换档前恢复未确认；保持关闭并先恢复原值。	Restoration before changing the limit is not confirmed; remain off and restore the original values first.
存在待恢复记录，不启用新的充电档位。	A pending recovery record exists; no new charge-limit setting will be enabled.
旧限充会话尚未恢复；不启用排队的新档位。	The previous charge-limit session has not been restored; the queued setting will not be enabled.
关闭后的原值恢复尚未确认。	Restoration of the original values after switching off is not confirmed.
恢复操作结束，但原值仍未确认。	The restoration operation ended, but the original values are still not confirmed.
原值恢复尚未确认；请先使用恢复按钮。	Restoration of the original values is not confirmed; use Restore first.
水冷意外断开；10分钟内每3秒尝试重连同一设备。	Liquid cooling disconnected unexpectedly; retry connecting to the same device every 3 seconds for up to 10 minutes.
水冷已自动重连。	Liquid cooling reconnected automatically.
水冷重连等待已超过10分钟；请手动连接。	Liquid-cooling reconnection has waited more than 10 minutes; connect manually.
水冷重连已取消。	Liquid-cooling reconnection was cancelled.
水冷自动连接已取消。	Automatic liquid-cooling connection was cancelled.
实验性限充未连接；默认关闭。	Experimental charge limiting is not connected; off by default.
默认关闭；仅手动启用。本功能会覆盖平台状态。	Off by default; enable manually only. This feature overrides the platform state.
发现未确认恢复的实验会话。请先使用恢复按钮，不自动启用。	An experimental session has unconfirmed restoration. Use Restore first; it will not be enabled automatically.
正在启动独立监护并检查控制接口。	Starting the independent guardian and checking the control interface.
正在检查控制接口与原值；尚未确认生效。	Checking the control interface and original values; effectiveness has not been confirmed.
正在停止实验性限充并恢复原值。	Stopping experimental charge limiting and restoring the original values.
正在核对旧会话并恢复原值。	Checking the previous session and restoring the original values.
没有需要恢复的实验会话；保持关闭。	No experimental session needs restoration; remaining off.
实验未启用，原值未改动。	The experiment was not enabled; original values were not changed.
实验性限充运行中：已达上限，当前未充电。	Experimental charge limiting is active: the limit is reached and the battery is not charging.
实验性限充运行中：低于上限，正在充电。	Experimental charge limiting is active: below the limit and charging.
实验性限充运行中：等待充电状态确认。	Experimental charge limiting is active: waiting for charging status confirmation.
工作进程已尝试恢复，监护正在复核。	The worker attempted restoration; the guardian is checking it.
实验性限充已停止；平台、阈值和缓存观察已恢复，不能排除全部持久化副作用。	Experimental charge limiting stopped; the platform, threshold and observed cache match their original values. Persistent side effects cannot be ruled out.
平台与实时阈值已恢复；固件缓存尚未确认一致，未写入缓存。	The platform and live threshold have been restored; the firmware cache has not been confirmed to match, and was not written.
恢复尚未确认。请勿再次启用，保留诊断日志并使用恢复按钮。	Restoration is not confirmed. Do not enable again. Keep the diagnostic logs and use Restore.
恢复尚未确认；独立监护可能仍在处理，请勿再次启用。	Restoration is not confirmed; the independent guardian may still be working. Do not enable again.
独立监护已退出，但恢复尚未确认。请使用恢复按钮。	The independent guardian exited, but restoration is not confirmed. Use Restore.
状态监测异常，已请求停止；请核对恢复。	Status monitoring failed and stop was requested; check restoration.
启用未完成，已请求恢复；请检查状态或使用恢复按钮。	Enabling did not complete and restoration was requested; check the status or use Restore.
先恢复上一会话；不会接管未知原值。	Restore the previous session first; unknown original values will not be taken over.
独立监护未启动，未确认启用。	The independent guardian did not start; enabling is not confirmed.
监护启动超时，已请求停止；请检查恢复状态。	The guardian start timed out and stop was requested; check restoration status.
原监护或工作进程仍在恢复，不并行写入。	The original guardian or worker is still restoring; no parallel writes will be made.
请先恢复原生工作站养护档及原始阈值；不强制接管。	Restore the native workstation care profile and original threshold first; they will not be forcibly taken over.
请连接电源并确认电池状态有效后，再手动启用。	Connect AC and confirm a valid battery state before enabling manually.
平台或阈值被外部改变；停止实验，不反复覆盖。	The platform or threshold changed externally; stop the experiment without repeatedly overwriting it.
已断开电源或电池状态无效，尝试恢复原值。	AC is disconnected or battery state is invalid; attempting to restore the original values.
相关状态在启用前改变；不写入。	Related state changed before enabling; no writes will be made.
启用期间收到停止请求。	A stop request was received while enabling.
监测中断或系统刚恢复，停止实验并恢复原值。	Monitoring was interrupted or the system just resumed; stop the experiment and restore the original values.
固件限充门控未开启，停止实验。	The firmware charge-limit gate is not enabled; stop the experiment.
阈值读取失败或格式改变。	The threshold could not be read or its format changed.
阈值由其他程序改变，不覆盖。	Another program changed the threshold; it will not be overwritten.
平台状态由其他程序改变，不覆盖。	Another program changed the platform state; it will not be overwritten.
阈值所有权已改变，不覆盖。	Threshold ownership changed; it will not be overwritten.
工作进程未及时退出，监护正在恢复；不能保证WMI provider已取消迟到调用。	The worker did not exit promptly and the guardian is restoring; cancellation of late WMI provider calls is not guaranteed.
无法确认工作进程已结束；不并行发起恢复。	Worker exit is not confirmed; no parallel restoration will be started.
界面或独立监护已退出，停止实验并恢复。	The UI or independent guardian exited; stop the experiment and restore.
另一个限充实验或恢复仍在运行。	Another charge-limit experiment or restoration is still running.
安静	Quiet
安装 / 修复	Install / repair
按键开关设置	Key controls
按键行为	Key behavior
办公→最佳能效，平衡/自定义→平衡，狂暴→最佳性能	Office: efficiency; Balanced/Custom: balanced; Turbo: performance
保存为 Windows 默认刷新率，重启后仍生效。自动省电切换不会更改此默认值。	Save as the Windows default after restart. Automatic switching will not change this default.
保存为我的方案	Save my preset
背景不透明度	Background opacity
冰海呼吸	Ice breathing
不区分 P / E 核；同时写入当前电源方案 AC / DC 的处理器 Class 0、Class 1 与 Class 2。仅在本次 RevoHelper 会话持有，退出或异常恢复时还原；部分固件仍可能忽略。	Applies to all processor classes on AC and battery. Held only for this session and restored on exit. Some firmware may ignore it.
橙色 #FF7000	Orange #FF7000
持续负载的主要功耗上限	Power limit for sustained workloads
触摸板开关	Touchpad switch
纯核显模式	iGPU only
此灯效使用固定配色，色块仅供预览。	This effect has fixed colors. The swatch is a preview only.
此设备未声明支持时不可用	Unavailable unless supported by this device
此水冷灯效使用固定颜色，不能自选配色。	This cooling effect uses fixed colors.
当前显示器	Current display
当前状态为注水模式	Priming is active
导出方案	Export preset
导入 .revolight.json 键盘方案	Import a .revolight.json keyboard preset
灯带	Light bar
灯效	Effect
登录后与睡眠唤醒时自动连接上次设备	Reconnect the last device after sign-in and wake
登录后自动连接水冷	Connect after sign-in
登录前自动连接	Connect before sign-in
电池状态	Battery protection
电源	Power
调整后自动应用	Changes apply automatically
顶部居中	Top center
底部居中	Bottom center
定时关机	Scheduled shutdown
动态节能	Dynamic power saving
动态节能说明	About dynamic power saving
可同时降低 CPU 功耗和温度	May also reduce CPU power use and temperature
独立	Discrete
独立监控小窗 · 不注入游戏	Floating monitor · No game injection
独立小窗 · 只读监控 · 不注入游戏	Floating window · Read-only · No game injection
独显直连	dGPU only
读取并控制官方键盘背光	View and control keyboard lighting
读取官方水冷连接与控制状态	View cooling connection and controls
短时睿频与突发负载的功耗上限	Power limit for short bursts
断开	Disconnect
分别设置每个区域的灯光。	Control each lighting zone separately.
分区即时编辑	Live zone editing
风扇会明显变响，关闭后恢复原档位。	Fans will get louder. Turn off to restore the current profile.
负载升高会恢复原限制；Windows 频率响应可能延迟 5–10 秒。	The previous limit returns under load; Windows may take 5–10 seconds to respond.
更安静	Quieter
工作站模式	Workstation
功耗由官方预设管理	Power is managed by the OEM preset
功能键屏显提示	Function-key on-screen prompts
关闭	Off
关机或休眠时为兼容设备保留 USB 供电	Keep USB power available when shut down or hibernating
关闭独显显示通路 · 仅在机型能力确认后开放	Disable discrete display output · Only on verified devices
关机下使用 USB 供电	USB charging while off
官方协议与状态回读均使用秒值；选择后自动应用，切换灯效不会改动此项。	Sleep time is in seconds. Changes apply automatically and are kept when switching effects.
官方预设仅供参考，自定义模式可以调整。	OEM presets are reference-only. Switch to Custom to edit.
还没有程序。点击“添加程序”选择它的 EXE。	No saved programs. Add a program by selecting its EXE.
核心频率不建议超过300MHz	Core offset: exceeding 300 MHz is not recommended
核心频率偏移	Core clock offset
红色 #FF0000	Red #FF0000
黄色 #FFFF00	Yellow #FFFF00
恢复 OEM 读取值并立即应用	Restore and apply OEM values
恢复该档位原厂默认曲线	Restore the profile's factory curve
恢复官方默认键盘灯效	Restore the factory keyboard effect
恢复机器默认灯光，不会应用下方自选参数。	Restore factory lighting without applying the editor values below.
恢复水冷模块状态	Reset the cooling module
恢复所选服务	Restore selected services
混合输出	Hybrid
机器最初始默认背光	Factory keyboard lighting
机械革命官方控制台（CCUWinUI）	MECHREVO Control Center (CCUWinUI)
集成	Integrated
检查状态	Check status
键盘	Keyboard
键盘背光总开关	Keyboard lighting power
键盘彩虹 · 平滑流动	Rainbow · Smooth flow
键盘灯光方案	Keyboard presets
键盘低亮静态 · 深蓝	Dim static · Deep blue
键盘青蓝呼吸 · 低干扰	Cyan breathing · Subtle
键盘与灯带自动熄灯	Keyboard and light-bar sleep timer
默认背光	Default backlight
默认方向	Default direction
从左到右	Left to right
从右到左	Right to left
从下到上	Bottom to top
从上到下	Top to bottom
接入电源适配器后自动启动设备	Start the laptop when AC power is connected
解锁后可直接拖动小窗。	Unlock to drag the monitor window.
仅本次会话持有；退出自动恢复。生效可能延迟约 5–10 秒	Session only; restored on exit. Applying may take 5–10 seconds.
仅供大致参考。用户可在风扇曲线上微调想要的噪音表现，改进温度控制。笔记本的低噪音、高性能与低温度几乎构成不可能三角。使用水冷（设备支持时）、空调冷气环境或风压式散热器等辅助散热手段，通常可以提升整机解热能力，并同时改善噪音控制、性能释放与温度表现。	A rough guide, not a benchmark. Noise, performance and temperature are a trade-off. A custom fan curve or better external cooling may improve the balance.
仅记录主动添加的程序及操作时间，保存在本机。	Only programs you add and their selection times are saved locally.
仅在自定义模式可调整。	Editable only in Custom mode.
精密式触摸板	Precision touchpad
静夜微光	Quiet night
均衡	Balanced
均衡模式	Balanced
不安装也会在登录后自动连接水冷；但安装该小组件后开机登录前就能连上。	The cooler connects after sign-in even without this; install the helper to connect before sign-in.
开启后可调整 TGP、Dynamic Boost 与频率偏移；关闭时统一锁定。	Unlocks TGP, Dynamic Boost and clock offsets. Turn off to lock all three.
开始倒计时	Start countdown
可能冲突服务检测	Other control software
可能有1-2秒的切换延迟	Profile switching may take 1–2 seconds.
可能改写功耗策略；可临时停止或恢复，不会修改开机启动类型。	May change power policy. Temporary stop/restore does not change its startup type.
可能无法调节超过机械革命官方预设的功耗 PL 值。例如平衡模式默认最高 85 W，该限制无法被突破，但可以向下调整。若想尝试自定义调整，请使用狂暴模式，或在自定义模式方案中细调。	OEM presets can impose power ceilings, such as 85 W in Balanced mode. Lower limits may work; use Turbo or Custom for more control.
空闲时临时限频，忙碌或切换档位即恢复；不改原设置。	Temporarily limits clocks when idle. Restores when busy or changing profiles; original settings are kept.
控制显卡可用的额外功耗，不代表实际耗电。	Controls extra GPU power headroom, not actual consumption.
来电自动开机	Power on with AC
蓝色 #5267FF	Blue #5267FF
蓝牙开关	Bluetooth switch
蓝牙设备	Bluetooth device
蓝牙无线电	Bluetooth radio
连接	Connect
连接记录与说明	Connection history and notes
连续拖动只提交最后一次结果；操作失败会保留提示。	Dragging submits the final value only. Failed operations keep their message.
亮度	Brightness
临时将笔记本风扇拉至最大转速	Temporarily run the laptop fans at full speed
绿色 #24D66A	Green #24D66A
没有可测画面时 FPS 显示为“—”，硬件信息仍会更新。	FPS shows “—” when no frames can be measured; hardware readings still update.
每 0.5% 转速变化的延迟，范围 0–5000 ms，步长 100 ms。	Delay for each 0.5% fan-speed change: 0–5000 ms in 100 ms steps.
面向长期连接电源的固定工作场景。	For a laptop usually connected to AC power.
默认关闭 · 适用于所有性能档位	Off by default · Available in every profile
默认七色	Default rainbow
内屏由独显直接输出 · 优先游戏性能	Internal display driven by the dGPU · Gaming priority
内置摄像头	Built-in camera
判定与恢复规则	When it limits and restores
其它工具	Other tools
启用风扇独立输出	Separate CPU / GPU fans
启用风扇转换延迟	Enable fan response delay
强冷	Full-speed fans
切换到自定义模式后可调整风扇。	Switch to Custom mode to edit the fans.
切换后需重启电脑。	Restart required after switching.
切换显卡输出后，重新打开本页刷新可用刷新率。	After changing GPU output, reopen this page to refresh available rates.
切换后请点“重启”才会生效；开着快速启动时，关机再开机不会生效。请先保存工作，RevoHelper 不会自动重启。	Choose Restart to apply the change; with Fast Startup on, shutting down and powering on does not apply it. Save your work first. RevoHelper will not restart automatically.
关机期间保持限充；下次打开 RevoHelper 时自动接续。	The limit stays applied while the laptop is off; RevoHelper carries on the next time it starts.
正在启动限充并检查固件。	Starting the charge limit and checking the firmware.
限充运行中：低于上限，正在充电。	Charge limit running: below the limit, charging.
限充运行中：已达上限，当前未充电。	Charge limit running: limit reached, not charging.
关机期间保持限充，最多 3 天。	The limit stays applied through the shutdown, for up to 3 days.
限充运行中：已请求停充，等待固件响应。	Charge limit running: hold requested, waiting for the firmware.
已开启：100% 不限制，按原生策略充电。	On: 100% sets no limit; charging follows the native policy.
限充已关闭；按原生策略充电。	Charge limit off; charging follows the native policy.
限充已关闭；恢复未确认，固件会在约 30 分钟内自行恢复正常充电。	Charge limit off; restoration unconfirmed. The firmware returns to normal charging by itself within about 30 minutes.
旧版限充仍在生效，请先恢复原值。	The earlier limit method is still applied; restore the original values first.
另一个限充会话或恢复仍在运行。	Another charge-limit session or restoration is still running.
无法启动限充进程。	Could not start the charge-limit process.
限充进程未能启动。	The charge-limit process did not start.
限充进程已退出。	The charge-limit process has exited.
限充启动超时。	Starting the charge limit timed out.
已断开电源或电池状态无效，停止并恢复。	Power was disconnected or the battery state is invalid; stopping and restoring.
平台状态被改变，停止并恢复。	The platform state changed; stopping and restoring.
固件计时出现意外数值，停止并恢复。	The firmware countdown shows an unexpected value; stopping and restoring.
切换显卡输出后需重启电脑。请先保存工作；RevoHelper 不会自动重启。	Changing GPU output requires a restart. Save your work; RevoHelper will not restart automatically.
青蓝色 #00B0FF	Cyan #00B0FF
取消关机	Cancel shutdown
日常推荐 · 核显负责桌面，独显按需工作	Recommended for everyday use · dGPU wakes on demand
散热策略档位调节	Cooling strategy
扫描	Scan
色彩管理	Color management
色彩校正	Color calibration
设备正在执行固定注水与排气流程，期间不可取消或切换其他水冷操作。	A fixed priming and air-purge sequence is running. Other cooling controls are unavailable until it finishes.
设备重设	Reset device
设置后会回读确认系统启动项。	The Windows startup entry is checked after saving.
深度睡眠	Deep sleep
剩余时间	Time remaining
使用 Windows 原生关机提醒	Uses the Windows shutdown countdown
使用官方背光电源控制。	Uses the OEM lighting power control.
始终置顶	Always on top
收起	Close panel
手动指定	Manual
首次注水与排气流程	Initial filling and air purge
刷新率	Refresh rate
刷新率由 Windows 当前显示路径实时枚举。	Available refresh rates come from the current Windows display path.
水泵状态	Pump status
水冷灯	Cooler light
水冷箱风扇状态	Cooler fan status
按所选时长进入深度睡眠	Enter deep sleep after the selected time
瞬时高负载功耗墙 (PL4)	Peak power limit (PL4)
速度	Speed
添加程序…	Add program…
停止所选服务	Stop selected services
图形	Graphics
拖动图中点位，或在下方输入转速比例	Drag the curve points or enter fan percentages below
网络摄像头开关	Camera switch
无线网络	Wireless network
系统工具	System tools
显存不建议超过1000MHz	Memory offset: exceeding 1000 MHz is not recommended
显存频率偏移	Memory clock offset
显示功能	Display features
显示设置	Display settings
显示性能监控小窗	Show performance monitor
显示样式	Appearance
小窗位置	Monitor position
校色模式	Color preset
性能监控	Performance monitor
需先在本页成功连接并记住设备，开机时也要开启水冷箱电源。安装仅创建 RevoHelper 的 SYSTEM 启动任务，不停止或禁用官方服务；移除保留日志。任务已安装不等于蓝牙连接已成功，请冷启动测试。	First connect and remember the device here, and keep it powered on during startup. Installation only creates RevoHelper's SYSTEM task; OEM services are unchanged. Removing it keeps logs. Test a cold boot to confirm the connection.
选择机械革命显示色彩预设	Select a MECHREVO display preset
选择刷新率 (Hz)	Refresh rate (Hz)
颜色	Color
耀世流光	Rainbow flow
移除	Remove
移除记录	Remove record
应用并保存	Apply and save
应用并回读	Apply and verify
应用已载入方案	Apply loaded preset
优先续航，适合经常依靠电池供电。	For longer battery runtime when frequently unplugged.
由机械革命 GCU 返回兼容水冷设备，不按具体笔记本型号硬编码	Compatible coolers are reported by GCU, not selected from a laptop-model list
游戏防误触	Avoid accidental presses in games
右上	Top right
右下	Bottom right
与运行中关闭独显的“核显省电”不同。	This differs from disabling the dGPU temporarily while Windows is running.
预计关机	Shutdown at
运行方向	Direction
运行中	Running
在续航能力和电池寿命之间取得平衡。	Balances battery runtime and battery lifespan.
暂停并恢复原限制	Pause and restore limit
长效模式	Battery life
只改变键盘灯光	Keyboard lighting only
只移除这里的记录，不会删除程序文件。	Removes this record only, not the program file.
智慧控制	Smart control
重设	Reset
注水模式	Priming
专用按键行为	Dedicated key behavior
转换延迟	Response delay
状态详情	Status details
紫色 #9A45FF	Purple #9A45FF
字号	Font size
自动跟随	Automatic
自动跟随当前使用的程序；也可指定一个程序。	Follow the active program, or choose a saved program.
自动刷新率	Automatic refresh rate
最大	Max
最近 10 分钟至少 90% 空闲时，临时限制至 3 GHz	Temporarily cap at 3 GHz after 10 minutes with at least 90% idle time
左上	Top left
左下	Bottom left
Copilot 键	Copilot key
CPU 风扇	CPU fan
CPU 高级性能菜单	CPU advanced controls
CPU 温度墙	CPU temperature limit
DC 使用最低刷新率，AC 恢复偏好刷新率	Lowest rate on battery; preferred rate on AC
Default 保持系统和显示器当前的默认色彩行为。	Default keeps the current system and display color behavior.
Disabled 会关闭睿频；其余选项决定 Windows 请求睿频的积极程度。写入后 RevoHelper 会延迟复查，以识别被其他程序覆盖的情况。	Disabled turns off CPU boost. Other options control how aggressively Windows requests boost. Settings are checked again after applying.
Fn 功能键	Fn function keys
Fn 组合 / F1–F12	Fn combinations / F1–F12
FPS 来源	FPS source
GPU 超性能开关	GPU tuning
GPU 风扇	GPU fan
Intel 平台服务（可选测试）	Intel platform services (optional test)
Mini LED 局部调光	Mini LED local dimming
OSD 开关	OSD switch
PL1 · 长时功耗限制	PL1 · Sustained power limit
PL2 · 短时功耗限制	PL2 · Short-term power limit
RevoHelper可以与官方控制台共存，不会停用或禁用它们。GCUBridge、GCUService 与 OSD 热键后台必须继续运行。	RevoHelper can coexist with the OEM console without stopping or disabling it. GCUBridge, GCUService and OSD hotkey services must keep running.
RevoHelper 可以与官方控制台共存。请保留 GCUBridge、GCUService 和 OSD 热键后台运行。	RevoHelper can coexist with the OEM console. Keep GCUBridge, GCUService and OSD hotkey services running.
Wi-Fi 开关	Wi-Fi switch
Win 键	Windows key
Windows 处理器 Boost 策略	Windows CPU boost policy
Windows 电源模式跟随	Follow Windows power mode
Windows 通用功能	Windows controls
Windows 通用最高处理器频率	Windows maximum CPU frequency
XtuService、dptftcs 与 ipfsvc 属于 Intel 平台服务，不是机械革命控制台。	XtuService, dptftcs and ipfsvc are Intel platform services, not the MECHREVO console.
监控字号	Monitor font size
锁定监控小窗位置	Lock monitor position
🔒 已锁定	🔒 Locked
🔓 可拖动	🔓 Unlocked
最小	Smallest
小	Small
中	Medium
大	Large
浅色	Light
深色	Dark
主题	Theme
界面字号	Interface text size
正在运行	Running
正在启动	Starting
正在停止	Stopping
正在继续	Resuming
正在暂停	Pausing
已暂停	Paused
状态不可用	Status unavailable
所选服务已恢复运行，并已重新检测状态。	The selected services are running; their states have been rechecked.
所选服务已停止，并已重新检测状态。	The selected services have stopped; their states have been rechecked.
未运行	Not running
未连接	Disconnected
已连接	Connected
已停止	Stopped
状态未知	Unknown
未设置	Not set
即将关机	Shutting down soon
启用	Enabled
未启用	Disabled
尚未选择	Not selected yet
办公模式	Office
平衡模式	Balanced
性能模式	Performance
狂暴模式	Turbo
狂暴模式 · 高能	Turbo · High
狂暴模式 · 静音	Turbo · Quiet
自定义模式	Custom
未知模式	Unknown profile
键盘背光	Keyboard backlight
海岸灯带	Light bar
注水进行中	Priming…
等待蓝牙设备扫描	Waiting for device scan
由机械革命 GCU 扫描并识别兼容蓝牙水冷	Compatible Bluetooth coolers are discovered through MECHREVO GCU
自定义风扇曲线	Custom fan curve
风扇曲线参考	Reference fan curve
调整后会自动应用；温度越高，风扇转速应越高。	Changes apply automatically. Fan speed should rise with temperature.
官方的预设通常无法调整风扇曲线，所以仅作参考。	OEM preset fan curves are usually read-only and shown for reference.
当前自定义模式可以调整功耗。	Power limits can be edited in this Custom profile.
当前模式可以调整 CPU 功耗，调整后会检查是否生效。	CPU power limits can be edited and will be verified after applying.
当前模式的功耗由官方预设管理，显示的数值仅供参考，不能调整。	Power limits are managed by the OEM preset; these values are read-only.
Windows 处理器策略或 OEM 功耗控制后端已确认可用。	Windows CPU policy or OEM power control is available.
尚未确认可用的处理器控制后端。	No compatible CPU control backend has been verified.
正在探测当前设备是否支持此功能。	Checking whether this device supports the feature.
重新检测	Recheck
设备与功能检测	Device and feature detection
设备与功能状态	Device and feature status
按键与设备状态	Keyboard and device status
电源与电池状态	Power and battery status
按键状态	Keyboard status
设备开关状态	Device switch status
键盘灯光状态	Keyboard lighting status
电池保护状态	Battery protection status
电源设置状态	Power settings status
快捷设置状态	Shortcut settings status
充电上限状态	Charge limit status
正在检测…	Checking…
当前控制接口尚未确认支持该狂暴子策略；未发送指令。	This Turbo sub-strategy has not been confirmed by the control interface; no command was sent.
尚未收到可用档位信息；基础档位仍可手动尝试。	Mode information has not been reported; basic modes can still be tried manually.
尚未收到此功能的运行时证据。	Runtime evidence for this feature has not been reported.
正在重新检测设备与官方控制组件…	Rechecking the device and OEM control components…
正在检测设备与档位，请稍候；无需重复点击。	Checking the device and modes. Please wait; no need to click again.
设备身份尚未识别成功；请重新检测，不代表机型不支持。	Device identity could not be confirmed. Recheck; this does not mean the model is unsupported.
当前设备未识别为机械革命，暂不开放 OEM 控制。	This device was not identified as MECHREVO. OEM controls remain disabled.
官方控制组件未确认完整；请确认官方控制台及其服务可用后重新检测。	Required OEM components have not all been confirmed. Check the official control center and its services, then recheck.
已识别官方控制组件，尚未收到可用档位；请确认服务连接后重新检测。	OEM components were found, but no available modes have been reported. Check the service connection, then recheck.
尚未收到此档位的可用证据；可以重新检测，暂不代表机型不支持。	This mode has not been confirmed yet. Recheck; this does not mean the model is unsupported.
自定义档位或风扇控制尚未确认，曲线仅供查看。	Custom mode or fan control has not been confirmed. The curve is read-only.
已通过当前设备的运行时探测。	Verified on this device.
控制链存在，但尚未收到此功能的运行时证据。	A control interface is present, but support is not yet verified.
当前设备未确认支持此功能。	This feature has not been verified on this device.
本地草稿有效 · PL1 不高于 PL2	Draft valid · PL1 does not exceed PL2
数值无效 · 请检查 5–250 W 范围与 PL1 / PL2 顺序	Invalid values · Check the 5–250 W range and PL1 / PL2 order
自定义模式 · CPU / GPU 高级设置	Custom · Advanced CPU / GPU settings
调整后自动应用到当前方案。	Changes apply to the current profile automatically.
请切换到自定义模式后调整这些参数。	Switch to Custom mode to edit these settings.
仅在自定义模式可调整	Editable only in Custom mode
正在读取风扇曲线…	Reading the fan curve…
示意曲线，实际转速由电脑控制。	Reference curve; actual fan speed is controlled by the laptop.
正在初始化设备状态…	Initializing device status…
等待读取官方风扇表	Waiting for the fan table
等待读取当前显卡输出模式	Waiting for GPU output mode
等待读取当前 Windows 频率上限	Waiting for the Windows frequency limit
选择服务后可临时停止或恢复运行。	Select a service to temporarily stop or restore it.
等待读取灯光设备能力。	Waiting for lighting capabilities.
等待读取官方按键与背光状态。	Waiting for key and backlight status.
等待读取官方水冷状态。	Waiting for cooling status.
等待读取官方电池保护状态。	Waiting for battery protection status.
选择色彩模式后会调用官方校准接口。	Choose a color mode to apply through the OEM interface.
等待读取	Waiting for a reading
打开性能调整后读取当前 OEM 模式的高级参数。	Open Performance to read the current profile's advanced settings.
等待读取强冷状态。	Waiting for full-speed fan status.
选择时长后由 Windows 设置关机提醒。	Choose a duration for the Windows shutdown countdown.
等待读取 GCU 控制通道状态。	Waiting for the GCU connection status.
OEM 功耗档案暂未读取 · 当前仅保留本地草稿	OEM power profile not yet read · Showing the local draft
监控已关闭。	Monitor is off.
请先添加并选择一个程序；没有目标时 FPS 显示为“—”。	Add and select a program first. Without a target, FPS shows “—”.
正在启动帧率采集…	Starting frame capture…
显示程序提交的帧率；插帧后的帧数可能不同。	Shows application-presented frames; frame-generation results may differ.
帧率暂不可用，硬件读数不受影响。	FPS is unavailable; hardware readings are unaffected.
目标程序已退出，等待读取新的前台程序。	The target exited. Waiting for a new foreground program.
等待有效帧数据；暂时无法测量时 FPS 显示为“—”。	Waiting for frames. FPS shows “—” until it can be measured.
动态节能尚未连接。	Dynamic power saving is not connected.
动态节能未启用。	Dynamic power saving is off.
当前临时上限：3 GHz（不提高已有更低限制）	Temporary limit: 3 GHz (does not raise an existing lower limit)
尚无开机连接记录。	No startup connection record yet.
RevoHelper可以与官方控制台共存，不会停用或禁用它们	RevoHelper can coexist with the OEM console without stopping or disabling it.
设置已打开 · 可管理启动、冲突服务与界面选项	Settings open · Startup, other services and interface options
设置保存失败 · 未修改现有状态	Settings could not be saved · Existing settings unchanged
自动刷新率已开启：DC 使用最低刷新率，AC 恢复偏好刷新率。	Automatic refresh rate is on: lowest rate on battery, preferred rate on AC.
自动刷新率已关闭。	Automatic refresh rate is off.
我的灯光方案已保存；可继续导出为 .revolight.json 分享。	Your lighting preset was saved. Export it as .revolight.json to share.
当前方案的风扇设置待读取，请重新打开风扇调整。	Fan settings have not been read. Reopen Fan control.
曲线校验未通过 · 请检查递增关系与末端 100%	Invalid curve · Check increasing values and the final 100% point
本机未开放核心负偏移。	Negative core offsets are unavailable on this device.
偏移范围待读取；负值仅在 OEM 支持时可用。	Waiting for offset limits. Negative values require OEM support.
添加需要监控的程序	Add a program to monitor
可执行程序 (*.exe)|*.exe	Executable programs (*.exe)|*.exe
导入 RevoHelper 键盘灯光方案	Import a RevoHelper keyboard preset
导出 RevoHelper 键盘灯光方案	Export a RevoHelper keyboard preset
RevoHelper 键盘灯光方案 (*.revolight.json)|*.revolight.json|JSON 文件 (*.json)|*.json	RevoHelper keyboard preset (*.revolight.json)|*.revolight.json|JSON files (*.json)|*.json
RevoHelper 键盘灯光方案 (*.revolight.json)|*.revolight.json	RevoHelper keyboard preset (*.revolight.json)|*.revolight.json
选择后立即生效，默认 100%。	Applies immediately; default is 100%.
关闭窗口	Close
窗口缩放	Window scale
设置独立监控小窗的位置和显示内容。	Choose the floating monitor's position and readings.
性能监控设置已展开	Performance monitor settings open
单色静态	Static
呼吸	Breathing
波浪	Wave
彩虹	Rainbow
单点亮	Reactive
涟漪	Ripple
雨滴	Raindrop
七彩跑马	Color marquee
烟火	Fireworks
极光	Aurora
游戏	Game
冲击	Impact
流星	Meteor
多彩	Colorful
旋转	Rotate
LOGO 灯	Logo light
已启用随 Windows 启动	Start with Windows enabled
已关闭随 Windows 启动	Start with Windows disabled
界面预览已切换 · 未向硬件发送指令	Preview changed · No hardware commands sent
正在切换整机档位并等待 GCU 回读…	Switching profile and waiting for GCU confirmation…
档位已切换，正在复查 Windows 频率策略…	Profile changed; checking Windows frequency policy…
已同步显示当前 OEM 档位；未向硬件追加写入。	Current OEM profile synchronized; no extra hardware writes.
已同步当前 OEM 档位，正在核对对应的 Windows 频率策略…	OEM profile synchronized; checking its Windows frequency policy…
OEM 档位与对应的 Windows 频率策略已同步。	OEM profile and Windows frequency policy are synchronized.
检测到上一档位遗留的频率限制，已按当前 OEM 档位恢复。	Removed a leftover frequency limit from the previous profile.
只读传感器暂不可用 · 将自动重试	Sensor readings unavailable · Retrying automatically
已读取当前方案的风扇设置	Current profile's fan settings read
已读取官方风扇设置	OEM fan settings read
已读取官方风扇曲线 · 当前控制器不可用	Fan curve read · Writing is unavailable
已读取风扇曲线 · 调整后自动应用	Fan curve read · Changes apply automatically
已读取风扇曲线 · 官方预设仅供参考	Fan curve read · OEM preset is reference-only
已恢复该档位原厂默认曲线 · 未写入硬件	Factory curve restored to the draft · No hardware writes
请切换到自定义模式后调整风扇。	Switch to Custom mode to adjust the fans.
请切换到自定义模式后恢复默认曲线。	Switch to Custom mode to restore the fan curve.
狂暴 · 静音策略预览 · 未向硬件发送指令	Turbo Quiet preview · No hardware commands sent
狂暴 · 高能策略预览 · 未向硬件发送指令	Turbo High preview · No hardware commands sent
已恢复当前档位 OEM 功耗读取值 · 未写入硬件	OEM power values restored to the draft · No hardware writes
性能调整面板已展开	Performance settings open
官方灯光控制通道未接入。	OEM lighting control is unavailable.
官方快捷控制通道未接入。	OEM quick controls are unavailable.
Windows 通用开关与官方按键状态已读取。	Windows switches and OEM key status read.
强冷运行中，风扇保持最大转速。	Full-speed cooling is on.
强冷已关闭，风扇由当前 OEM 档位管理。	Full-speed cooling is off; the current OEM profile controls the fans.
当前机型未返回强冷支持状态。	Full-speed cooling is not verified on this model.
当前控制链未提供强冷接口。	Full-speed fan control is unavailable.
风扇抽拉面板已展开 · 正在等待读取	Fan panel open · Waiting for readings
风扇曲线已读取 · 当前控制器不可用	Fan curve read · Writing is unavailable
风扇曲线已读取 · 可应用并回读确认	Fan curve read · Apply and verify changes
OEM 未返回自定义高级参数，保留当前草稿。	OEM advanced values are unavailable; the draft is kept.
OEM 当前模式未返回 GPU 高级字段；当前预设保持固件只读。	Advanced GPU values are unavailable; this OEM preset remains read-only.
已读取当前 OEM 模式的 GPU 字段，仅供参考；高级参数仅在自定义模式可调整。	GPU values read for reference. Advanced settings are editable in Custom mode only.
OEM 预设的 CPU / GPU 高级参数由固件锁定；仅在自定义模式允许调整。	OEM CPU / GPU settings are firmware-locked. Edit them in Custom mode.
命令已提交；OEM 状态尚未同步，保留本次输入值。	Command sent; waiting for OEM status. Your input is kept.
当前系统忙碌，保持原频率限制。	The system is busy; keeping the original frequency limit.
CPU 正在工作，保持原频率限制。	The CPU is busy; keeping the original frequency limit.
GPU 正在工作，保持原频率限制。	The GPU is busy; keeping the original frequency limit.
整套性能策略已应用、回读，并在 1 秒复查后保持不变。	Performance settings applied, read back and confirmed after one second.
整机档位已应用并回读。	Profile applied and verified.
整机档位与 Windows 频率策略已应用，并在延迟复查后保持一致。	Profile and Windows frequency policy applied and verified after a delay.
检测到 OEM 延迟覆盖，已重新应用 Windows 频率策略并回读确认。	OEM overwrote the delayed policy; Windows frequency settings were reapplied and verified.
外观	Appearance
水冷已连接，已读取风扇、水泵与散热策略状态。	Cooling connected; fan, pump and cooling strategy read.
未检测到连接的官方水冷设备。	No connected OEM cooler detected.
显示模式列表已刷新。	Display modes refreshed.
未收到灯光状态；请确认 GCUBridge 正在运行后重试。	No lighting status received. Check that GCUBridge is running and retry.
未安装	Not installed
已读取当前自定义方案的 CPU / GPU 高级参数。	Current Custom profile's advanced CPU / GPU settings read.
已读取高级参数；GPU 频率范围未确认，偏移调整暂不可用。	Advanced settings read; GPU offset limits are unverified, so offset controls are unavailable.
自定义 CPU / GPU 参数已写入并从 OEM 状态回读确认。	Custom CPU / GPU settings applied and verified through OEM readback.
当前 OEM 模式的 GPU 参数已写入并回读确认。	GPU settings applied and verified for the current OEM profile.
GPU 频率偏移已与 OEM 实时读数一致。	GPU offsets already match the live OEM readings.
CPU/GPU 风扇曲线与响应设置已写入并回读确认。	CPU/GPU fan curves and response settings applied and verified.
恢复命令已发送，已读取当前曲线；原厂默认值未确认。	Reset sent and current curve read; factory defaults are not yet verified.
Windows 睿频策略已应用；保留用户已有的处理器上限。	Windows boost policy applied; existing CPU limits are preserved.
没有待恢复的处理器兼容限制。	No temporary CPU compatibility limit to restore.
已恢复临时处理器兼容限制；外部修改保持不变。	Temporary CPU compatibility limit restored; external changes preserved.
核心负值须经 OEM 回读确认，部分机型会拒绝。	Negative core offsets require OEM confirmation; some models reject them.
正在通过机械革命 GCU 扫描兼容蓝牙水冷设备…	Scanning for compatible Bluetooth coolers through GCU…
暂未发现兼容水冷；请确认水冷已通电并靠近电脑，然后重新扫描。	No compatible cooler found. Check that it is powered on and nearby, then scan again.
水冷已连接，已读取设备状态。	Cooler connected and device status read.
GCU 已记住此设备，但扫描列表暂为空；可以点击连接重试。	GCU remembers this device but the scan list is empty. Try Connect again.
正在识别设备	Identifying device
正在识别处理器	Identifying processor
程序退出，独立监护继续恢复原值。	The app is exiting; the independent guardian continues restoring the original values.
遥测不可用，动态节能已暂停。	Telemetry is unavailable; dynamic power saving is paused.
供电或睡眠状态发生变化，动态节能已暂停。	Power or sleep state changed; dynamic power saving is paused.
暂未读取	Not read yet
设备识别暂不可用	Device identification unavailable
处理器识别暂不可用	Processor identification unavailable
遥测处理积压，动态节能已暂停。	Telemetry is backed up; dynamic power saving is paused.
遥测已中断，动态节能已暂停。	Telemetry stopped; dynamic power saving is paused.
程序退出，恢复原始频率限制。	The app is exiting; restoring the original frequency limit.
点击检查，读取登录前任务状态。	Select Check status to read the pre-sign-in task.
性能设置已改变，动态节能保持开启并重新观察。	Performance settings changed; dynamic power saving stays on and starts observing again.
颜色格式无效，请输入 #RRGGBB；尚未发送。	Invalid color. Enter #RRGGBB; nothing was sent.
当前键盘仅确认支持背光开关，未发送灯效、颜色或亮度配置。	This keyboard only confirmed a backlight switch; no effect, color or brightness was sent.
调整后自动应用并回读	Changes apply and verify automatically
灯区已关闭：参数仅保存，打开后自动应用	Zone is off: settings are saved and apply when it turns on
档位切换已取消，正在同步实际档位。	Profile switch cancelled; syncing the actual profile.
基础狂暴档位未确认，未应用后续策略。	Basic Turbo was not confirmed; follow-up policies were not applied.
已有更新的档位请求，跳过旧档位复查。	A newer profile request exists; skipping the check of the older one.
正在核对并恢复上次控制状态，尚未发送档位指令…	Checking and restoring the previous control state; no profile command sent yet…
正在准备切换整机档位…	Preparing to switch the profile…
已启用水冷自动连接；登录与睡眠唤醒后会尝试连接上次设备。	Cooler auto-connect is on; the last device will be tried after sign-in and wake.
已关闭水冷自动连接；仍可在本页手动连接。	Cooler auto-connect is off; you can still connect on this page.
当前已启用	Currently on
当前未启用	Currently off
当前启动项状态无法确认	The startup state cannot be confirmed
性能调整未执行：限充正在切换或恢复，请等待完成后再调整。	Performance change not applied: charge limiting is switching or restoring. Wait until it finishes.
动态频率恢复未完成，请在更多方案中重试恢复后再调整。	Dynamic frequency restoration did not finish. Retry it in More profiles before changing settings.
本机风扇写入控制器不可用。	Fan control is unavailable on this device.
未取得完整风扇曲线与响应设置，请稍后重试。	The full fan curves and response settings were not read. Try again later.
官方控制中心	Control Center
官方 GCU 状态通道暂不可用 · 将自动重试	OEM GCU status is unavailable · Retrying automatically
OEM 功耗档案暂不可用 · 性能调整保持安全锁定	OEM power profiles unavailable · Performance changes stay locked
性能控制通道未接入	Performance control is not connected
GCU 未返回 CPU 功耗状态	GCU did not report CPU power status
已读取 Custom 的 GCU PL 配置、Boost 与频率策略	Read Custom GCU power limits, boost and frequency policy
已读取 CPU 直连 PL1 / PL2、Boost 与频率策略	Read CPU PL1 / PL2, boost and frequency policy
已读取 OEM 固件预设、Boost 与频率策略；预设 PL 不提供硬件回读	Read the OEM preset, boost and frequency policy; preset power limits have no hardware readback
水冷自动连接已关闭。	Cooler auto-connect is off.
官方水冷控制通道未接入。	OEM cooler control is not connected.
水冷自动连接当前不可执行。	Cooler auto-connect cannot run now.
断开水冷失败	Disconnecting the cooler failed
启动注水模式失败	Starting priming failed
重设水冷失败	Resetting the cooler failed
电源与电池状态已读取。	Power and battery status read.
已读取当前显示器、分辨率与可用刷新率。	Read the current display, resolution and available refresh rates.
显示器、刷新率与 OEM 显示功能状态已读取。	Display, refresh rates and OEM display features read.
显卡输出控制器未接入	GPU output control is not connected
GCU 未返回可识别的显卡输出模式	GCU did not report a recognized GPU output mode
PL1 / PL2 回读不可用	PL1 / PL2 readback unavailable
OEM 预设 PL 未写入	OEM preset power limits not written
服务状态尚未确认	Service status is not confirmed yet
保存的程序路径过长，请减少记录后重试。	The saved program list is too long. Remove some entries and try again.
请选择完整的 EXE 程序路径。	Choose a full path to an EXE program.
最多可保存 64 个程序，请先移除不需要的记录。	Up to 64 programs can be saved. Remove entries you no longer need.
定时关机仅支持 5、15、30、60 或 120 分钟。	Scheduled shutdown supports 5, 15, 30, 60 or 120 minutes.
设置定时关机失败	Setting the scheduled shutdown failed
取消定时关机失败	Cancelling the scheduled shutdown failed
没有需要恢复的临时刷新率。	No temporary refresh rate to restore.
显示设置已由其它操作更改，保留当前状态。	Display settings were changed elsewhere; keeping the current state.
原刷新率在当前路径已不可用，未强制恢复。	The original refresh rate is no longer available on this display path; it was not forced back.
内置显示器	Built-in display
显示控制会话正在结束。	The display control session is ending.
未找到内嵌的 FPS 采集组件。	The bundled FPS capture component was not found.
FPS 采集组件未能启动。	The FPS capture component did not start.
固定的程序已退出，请重新选择。	The selected program exited. Choose it again.
当前自定义方案已切换或无法确认，已停止写入及跨方案恢复。	The custom preset changed or cannot be confirmed; writing and cross-preset restoring stopped.
当前 CPU 功耗回读不完整或无效，已停止写入及恢复。	CPU power readback is incomplete or invalid; writing and restoring stopped.
PL1 不能高于 PL2。	PL1 cannot be higher than PL2.
当前测试版尚未开放 CPU 频率上限写入。	This build does not write CPU frequency limits yet.
未取得完整 CPU 功耗回读或当前自定义方案身份，未写入。	CPU power readback or the custom preset was not fully read; nothing was written.
本次未发送功耗写入。	No power limit was sent this time.
已恢复原值。	Original values restored.
检测到外部 CPU 功耗修改，已保留当前值；未确认恢复原值。	An external CPU power change was detected and kept; restoring the original value is not confirmed.
CPU 功耗原值已恢复并回读确认。	Original CPU power limits restored and verified.
当前自定义方案已变化或未确认；未写入草稿。	The custom preset changed or is not confirmed; the draft was not written.
性能档位或自定义方案已切换，已停止本次参数写入；请重新读取状态。	The profile or custom preset changed; this write stopped. Read the status again.
OEM 未返回该参数的实时状态；未写入。	OEM did not report this setting's live state; nothing was written.
未读取到完整的 OEM GPU 功耗范围；未写入。	The OEM GPU power range was not fully read; nothing was written.
TGP 超出当前 OEM 范围，或使能状态未知；未写入。	TGP is outside the OEM range or its enable state is unknown; nothing was written.
请先开启 GPU 超性能开关，并等待读取频率偏移范围。	Turn on GPU boost first and wait for the offset range to be read.
GPU 频率偏移超出当前 OEM 可调范围；未写入。	GPU offsets are outside the OEM range; nothing was written.
开关值必须为 0 或 1；未写入。	Switch values must be 0 or 1; nothing was written.
未确认当前为自定义模式；未写入高级参数。	Custom mode is not confirmed; advanced settings were not written.
该参数已与 OEM 实时读数一致。	This setting already matches the live OEM reading.
本次单项参数已发送并由 OEM 回读确认。	Setting sent and verified through OEM readback.
驱动功率上限不可用，实际效果未确认。	The driver power limit is unavailable; the actual effect is not confirmed.
本次单项参数已发送，但 OEM 回读未匹配；未确认生效。	Setting sent, but the OEM readback did not match; not confirmed.
OEM 回读确认；NVIDIA 驱动功率上限暂不可读，实际效果未确认。	Verified by OEM readback; the NVIDIA driver power limit cannot be read, so the actual effect is not confirmed.
仅在自定义模式且 GPU 超性能开关开启时允许调整频率偏移。	Offsets can be changed only in Custom mode with GPU boost on.
GPU 频率偏移超出当前 OEM 可调范围，或未读取到范围；未写入。	GPU offsets are outside the OEM range, or the range was not read; nothing was written.
GPU 频率偏移已发送，但 OEM 回读未匹配；未确认生效。	GPU offsets sent, but the OEM readback did not match; not confirmed.
当前自定义方案已变化；未写入草稿。	The custom preset changed; the draft was not written.
CPU 温度墙必须在 70–105°C 之间。	The CPU temperature limit must be 70–105 °C.
CPU 瞬时功耗墙必须在 5–400 W 之间。	The CPU burst power limit must be 5–400 W.
GPU TGP 必须在 25–250 W 之间。	GPU TGP must be 25–250 W.
GPU 频率偏移超出安全输入范围。	GPU offsets are outside the safe input range.
强冷已经开启。	Full-speed fans are already on.
强冷已经关闭。	Full-speed fans are already off.
强冷已开启并回读确认。	Full-speed fans on and verified.
强冷已关闭并回读确认。	Full-speed fans off and verified.
强冷状态未确认，已恢复操作前状态。	Full-speed fan state not confirmed; the previous state was restored.
强冷状态未确认，恢复操作前状态也未取得一致回读。	Full-speed fan state not confirmed, and restoring the previous state was not verified either.
风扇曲线必须包含 16 个点位。	A fan curve must have 16 points.
GCU 未返回当前风扇方案名称，无法恢复原厂曲线。	GCU did not report the current fan profile, so the factory curve cannot be restored.
未确认当前自定义方案；未发送恢复命令。	The custom preset is not confirmed; no reset was sent.
当前风扇方案已变化；未恢复旧方案。	The fan profile changed; the older profile was not restored.
已发送恢复命令，但当前曲线未确认；请重新读取。	Reset sent, but the current curve is not confirmed. Read it again.
GCU 未返回当前风扇方案名称，不能安全写入。	GCU did not report the current fan profile, so it cannot be written safely.
风扇转换延迟必须位于 0–5000 ms/0.5%。	The fan response delay must be 0–5000 ms/0.5%.
未确认当前自定义方案；未写入风扇设置。	The custom preset is not confirmed; fan settings were not written.
未取得完整原厂风扇回读，已阻止写入。	The factory fan readback is incomplete; writing was blocked.
当前风扇方案已变化；未写入旧方案草稿。	The fan profile changed; the older draft was not written.
GCU 风扇回读未与目标设置收敛。	GCU fan readback did not match the target settings.
性能档位或自定义方案已切换，已停止写入；未向其它方案恢复旧曲线。	The profile or custom preset changed; writing stopped and no curve was restored to another profile.
GCU 回读未收敛到目标模式	GCU readback did not reach the target mode
狂暴子策略未收敛	Turbo strategy did not settle
自定义方案编号必须位于 1–5。	The custom preset number must be 1–5.
当前 GCU 尚未确认支持狂暴模式的高能/静音子策略。	GCU has not confirmed support for the Turbo performance/quiet strategies.
水冷设备标识不能为空。	The cooler ID cannot be empty.
机器初始默认背光只适用于键盘分区。	Factory default lighting applies only to the keyboard.
水冷灯不支持所选灯效。	The cooler light does not support this effect.
深度睡眠等待时间必须在 5–120 分钟之间。	The deep-sleep delay must be 5–120 minutes.
深度睡眠已关闭并回读确认。	Deep sleep off and verified.
深度睡眠写入后未得到目标状态回读。	The deep-sleep change was sent, but the readback did not confirm it.
充电上限必须在 50%–100% 之间。	The charge limit must be 50%–100%.
键盘背光已开启并回读确认。	Keyboard backlight on and verified.
键盘背光已关闭并回读确认。	Keyboard backlight off and verified.
键盘背光写入后未得到目标状态回读。	The keyboard backlight change was sent, but the readback did not confirm it.
水冷灯未返回当前配置，无法安全切换总开关。	The cooler light did not report its settings, so it cannot be switched safely.
灯光亮度必须为 0–4，速度必须为 1–4。	Brightness must be 0–4 and speed 1–4.
LOGO 关闭未得到回读确认。	Logo off was not confirmed by readback.
LOGO 关闭已请求，但海岸灯补偿未得到回读确认。	Logo off was requested, but turning the light bar back on was not confirmed.
LOGO 已关闭，已补发海岸灯开启命令；请以实体灯为准。	Logo is off and the light bar was turned back on; check the light itself.
LOGO 已关闭，未改动海岸灯开关。	Logo is off; the light bar switch was not changed.
灯效休眠时间不是官方支持的档位，未发送命令。	This sleep time is not an official option; nothing was sent.
键盘与灯带自动休眠已关闭并回读确认。	Keyboard and light-bar sleep timer off and verified.
休眠命令已发送，但官方状态未返回 CloseTimer，无法确认。	Sleep timer sent, but the OEM status did not report CloseTimer; not confirmed.
电池保护模式已应用并回读确认。	Battery protection applied and verified.
电池保护模式写入后未得到目标状态回读。	The battery protection change was sent, but the readback did not confirm it.
色彩管理已关闭并回读确认。	Color management off and verified.
关闭色彩管理命令已提交，官方状态仍在更新。	Color management off was sent; the OEM status is still updating.
未检测到已连接的官方水冷设备，未发送控制命令。	No connected OEM cooler detected; no command was sent.
水冷风扇策略已应用并回读确认。	Cooler fan mode applied and verified.
水冷风扇策略写入后未得到目标状态回读。	The cooler fan change was sent, but the readback did not confirm it.
水冷泵策略已应用并回读确认。	Pump mode applied and verified.
水冷泵策略写入后未得到目标状态回读。	The pump change was sent, but the readback did not confirm it.
请先选择扫描到的水冷设备。	Select a scanned cooler first.
使用 GCU 已记住的水冷设备，未改写设备记录。	Using the cooler GCU remembers; the device record was not changed.
所选设备不在本次 GCU 扫描结果中，未发送命令。	The selected device is not in this GCU scan; no command was sent.
水冷设备已选择并回读确认。	Cooler selected and verified.
设备选择写入后未得到目标状态回读。	The device selection was sent, but the readback did not confirm it.
水冷设备尚未连接，未启动注水模式。	The cooler is not connected; priming was not started.
当前水冷状态不允许控制，未启动注水模式。	The cooler cannot be controlled now; priming was not started.
当前已经处于注水模式，请等待流程自动结束。	Priming is already running. Wait for it to finish.
当前状态为注水模式，请等待流程自动结束。	Priming is running. Wait for it to finish.
注水模式命令已发送，请等待水冷设备完成固定流程。	Priming sent. Wait for the cooler to finish its routine.
当前水冷状态不允许重设，未发送命令。	The cooler cannot be reset now; no command was sent.
水冷设备记录已清除并回读确认。	Cooler record cleared and verified.
重设命令发送后，设备记录尚未清除。	Reset sent, but the device record is not cleared yet.
GCU 未返回水冷状态，未发送连接命令。	GCU did not report cooler status; no connect command was sent.
水冷设备已经连接。	The cooler is already connected.
水冷设备已经断开。	The cooler is already disconnected.
水冷正在注水，未发送连接操作命令。	The cooler is priming; no connection command was sent.
当前水冷状态不允许连接操作，未发送命令。	The cooler cannot be connected or disconnected now; no command was sent.
水冷设备已连接并回读确认。	Cooler connected and verified.
水冷设备已断开并回读确认。	Cooler disconnected and verified.
连接命令发送后未得到已连接状态。	Connect was sent, but the cooler did not report connected.
断开命令发送后未得到已断开状态。	Disconnect was sent, but the cooler did not report disconnected.
OSD 提示	OSD prompts
Fn 键	Fn key
蓝牙	Bluetooth
触摸板	Touchpad
网络摄像头	Webcam
关机 USB 供电	USB charging while off
未读取到完整的 OEM TGP 范围或使能状态；未写入。	The OEM TGP range or enable state was not fully read; nothing was written.
TGP 设置超出当前机型可尝试范围；未写入。	TGP is outside this model's range; nothing was written.
本次参数在写入前已被其它操作修改，未覆盖新的设置。	The setting changed elsewhere before writing; the newer value was kept.
原设置未改变。	The original settings did not change.
未确认恢复原值；已保留后续参数变化或停止向其它方案写入，请重新读取状态。	Restoring the original value is not confirmed; later changes were kept or writing stopped. Read the status again.
已恢复操作前本次修改的 OEM 设置。	The OEM settings changed by this operation were restored.
原有参数未改变。	The previous settings did not change.
驱动当前功率上限不可读，实际效果未确认。	The driver's current power limit cannot be read; the actual effect is not confirmed.
当前性能档位已变化，已停止写入。	The profile changed; writing stopped.
自定义方案已切换，已停止写入原方案参数。	The custom preset changed; writing to the previous preset stopped.
自定义方案已切换；未写入原方案参数。	The custom preset changed; the previous preset was not written.
请在自定义模式调整 GPU 参数。	Change GPU settings in Custom mode.
请在自定义模式开启 GPU 超性能，并等待读取实时状态。	Turn on GPU boost in Custom mode and wait for the live status.
GPU 参数或开关值无效；未写入。	Invalid GPU setting or switch value; nothing was written.
OEM 未返回本次参数的实时状态；未写入。	OEM did not report the live state of this setting; nothing was written.
GCU 性能状态回读超时，正在重连；本次操作未确认，未自动重发。	GCU performance readback timed out and is reconnecting; this operation is not confirmed and was not resent.
GCU 控制通道尚未连接，请稍后重试。	The GCU control channel is not connected yet. Try again later.
GCU 通道响应超时，正在重新连接；本次操作未确认，未自动重发。	GCU timed out and is reconnecting; this operation is not confirmed and was not resent.
睿频策略索引必须位于 0–6。	The boost policy index must be 0–6.
写入后回读不一致。	The readback after writing did not match.
恢复后回读不一致。	The readback after restoring did not match.
没有待恢复的处理器频率限制。	No processor frequency limit to restore.
已恢复 RevoHelper 会话前的处理器频率设置。	Processor frequency settings from before this RevoHelper session were restored.
解除频率上限后回读不一致，已保留恢复记录。	The readback after removing the frequency limit did not match; the recovery record was kept.
已保留损坏记录副本，并解除当前电源方案的频率上限。	A copy of the damaged record was kept and the frequency limit of the current power plan was removed.
最大处理器频率必须为 0（不限制）或 100–64000 MHz。	The maximum processor frequency must be 0 (no limit) or 100–64000 MHz.
RAPL 回读值与目标功耗不一致	RAPL readback does not match the target power
RAPL 写入后寄存器未收敛	RAPL registers did not settle after writing
PL1/PL2 必须为正数且 PL1 不高于 PL2。	PL1/PL2 must be positive, and PL1 cannot exceed PL2.
目标功耗超出当前处理器 RAPL 编码范围。	The target power is outside this processor's RAPL range.
读取灯光能力失败	Reading lighting capabilities failed
应用灯效失败	Applying the effect failed
灯光写入异常	Lighting write error
应用灯效休眠失败	Applying the sleep timer failed
灯效休眠	Sleep timer
应用灯光方案失败	Applying the lighting preset failed
保存灯光方案失败	Saving the lighting preset failed
导入灯光方案失败	Importing the lighting preset failed
导出灯光方案失败	Exporting the lighting preset failed
已保存的灯光方案无效	The saved lighting preset is invalid
恢复已保存灯光方案失败	Restoring the saved lighting preset failed
LOGO 关闭回读失败	Logo off readback failed
海岸灯补偿失败	Turning the light bar back on failed
我的灯光方案	My lighting preset
服务检测失败	Service check failed
服务恢复失败	Restoring services failed
服务停止失败	Stopping services failed
强冷切换失败	Switching full-speed fans failed
强冷状态读取失败	Reading full-speed fan status failed
读取风扇设置失败	Reading fan settings failed
风扇设置失败	Applying fan settings failed
恢复原厂风扇曲线失败	Restoring the factory fan curve failed
实时性能策略读取失败	Reading the live performance policy failed
性能策略应用异常	Performance policy error
高级参数读取失败	Reading advanced settings failed
单项参数写入失败	Applying the setting failed
高级参数写入失败	Applying advanced settings failed
GPU 频率偏移写入失败	Applying GPU offsets failed
读取水冷状态失败	Reading cooler status failed
水冷状态刷新失败	Refreshing cooler status failed
自动连接	Auto-connect
水冷自动连接失败	Cooler auto-connect failed
扫描水冷设备失败	Scanning for coolers failed
连接水冷失败	Connecting the cooler failed
读取 Windows 显示设置失败	Reading Windows display settings failed
刷新率已读取；OEM 显示功能状态读取失败	Refresh rates read; reading OEM display features failed
刷新显示模式失败	Refreshing display modes failed
系统开关切换失败	Switching the system toggle failed
电池保护切换失败	Switching battery protection failed
电源开关切换失败	Switching the power option failed
深度睡眠切换失败	Switching deep sleep failed
充电上限设置失败	Setting the charge limit failed
屏幕色彩切换失败	Switching the color preset failed
色彩管理切换失败	Switching color management failed
Mini LED 局部调光切换失败	Switching Mini LED local dimming failed
刷新率切换失败	Switching the refresh rate failed
自动刷新率已关闭，但临时状态恢复失败	Automatic refresh rate is off, but restoring the temporary state failed
自动刷新率应用失败	Applying automatic refresh rate failed
水冷风扇切换失败	Switching the cooler fan failed
水冷泵切换失败	Switching the pump failed
按键开关切换失败	Switching the key option failed
读取失败	Read failed
切换失败	Switch failed
整机档位读取失败	Reading the profile failed
已保留当前整机档位，后续策略未完全应用	Kept the current profile; follow-up policies were not fully applied
整机档位切换异常	Profile switch error
频率策略同步失败	Syncing the frequency policy failed
已同步 OEM 档位，但频率策略恢复失败	OEM profile synced, but restoring the frequency policy failed
已同步 OEM 档位，但核对频率策略时发生异常	OEM profile synced, but checking the frequency policy failed
控制会话恢复同步失败	Restoring the control session failed
检测到 OEM 延迟覆盖，但恢复失败	A delayed OEM override was detected, but restoring failed
恢复原值未确认	Restoring the original value is not confirmed
GPU 参数未完成	GPU settings did not complete
GPU 实时状态未获确认；未继续写入	GPU live status not confirmed; writing stopped
动态节能未完成	Dynamic power saving did not complete
恢复未完成	Restoration did not complete
频率恢复未完成	Frequency restoration did not complete
遥测处理异常	Telemetry error
水冷任务操作失败	Cooler task operation failed
FPS 采集暂不可用	FPS capture unavailable
监控暂不可用	Monitor unavailable
设置暂未保存	Settings not saved yet
程序记录暂未保存	Program list not saved yet
未能添加程序	Could not add the program
未能移除记录	Could not remove the entry
未选择程序	No program selected
Windows 显示控制器未接入。	Windows display control is not connected.
""";
}
