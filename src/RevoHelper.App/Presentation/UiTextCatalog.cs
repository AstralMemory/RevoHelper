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

    private static readonly (Regex Pattern, Func<Match, string> Format)[] Templates =
    [
        Rule(@"^部分状态读取失败，已保留已确认状态：(.+)$", m => "Some statuses could not be read; confirmed values were preserved: " + TranslateStatusDetails(m.Groups[1].Value)),
        Rule(@"^部分状态未读取：(.+)$", m => "Some statuses were not read: " + TranslateStatusDetails(m.Groups[1].Value)),
        Rule(@"^(按键与设备状态|电源与电池状态|按键状态|设备开关状态|键盘灯光状态|电池保护状态|电源设置状态|快捷设置状态|充电上限状态)读取失败：(.+)$", m => TranslateKnown(m.Groups[1].Value) + " could not be read: " + TranslateStatusDetails(m.Groups[2].Value)),
        Rule(@"^(按键状态|设备开关状态|键盘灯光状态|电池保护状态|电源设置状态|快捷设置状态|充电上限状态)未返回可识别状态。$", m => TranslateKnown(m.Groups[1].Value) + " did not return a recognized status."),
        Rule(@"^未运行：(.+)\n启动后会自动匹配该程序。$", m => $"Not running: {m.Groups[1].Value}\nIt will be matched when it starts."),
        Rule(@"^当前：([^\n]+)\n([\s\S]+)$", m => $"Current: {m.Groups[1].Value}\n{TranslateKnown(m.Groups[2].Value)}"),
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
        Rule(@"^已发现 (\d+) 个灯光分区；每个标签只控制对应硬件，键盘方案不会联动其它灯区。$", m => $"Found {m.Groups[1].Value} lighting zones. Each tab controls its own zone only."),
        Rule(@"^发现 (\d+) 个兼容水冷设备，请选择后连接。$", m => $"Found {m.Groups[1].Value} compatible coolers. Select one to connect."),
        Rule(@"^GCU 配置回读 · PL1 (.+) W · PL2 (.+) W · Boost (.+)$", m => $"GCU readback · PL1 {m.Groups[1].Value} W · PL2 {m.Groups[2].Value} W · Boost {m.Groups[3].Value}"),
        Rule(@"^GCU 配置回读 · (.+) · PL1 (.+) W（(启用|未启用)） · PL2 (.+) W（(启用|未启用)）$", m => $"GCU readback · {m.Groups[1].Value} · PL1 {m.Groups[2].Value} W ({TranslateKnown(m.Groups[3].Value)}) · PL2 {m.Groups[4].Value} W ({TranslateKnown(m.Groups[5].Value)})"),
        Rule(@"^OEM 固件预设 · GCU 配置回显 PL1 (.+) W（(启用|未启用)） / PL2 (.+) W（(启用|未启用)）· 非实际硬件 PL$", m => $"OEM preset · Reported PL1 {m.Groups[1].Value} W ({TranslateKnown(m.Groups[2].Value)}) / PL2 {m.Groups[3].Value} W ({TranslateKnown(m.Groups[4].Value)}) · Not measured hardware limits"),
        Rule(@"^频率回读 · C0 (.+) · C1 (.+) · C2 (.+) MHz$", m => $"Frequency readback · C0 {m.Groups[1].Value} · C1 {m.Groups[2].Value} · C2 {m.Groups[3].Value} MHz"),
        Rule(@"^已应用并回读 · PL1 (.+) W / PL2 (.+) W · (.+)$", m => $"Applied and verified · PL1 {m.Groups[1].Value} W / PL2 {m.Groups[2].Value} W · {m.Groups[3].Value}"),
        Rule(@"^已应用并回读 Boost · (.+)；OEM 预设 PL 未改动$", m => $"Boost applied and verified · {m.Groups[1].Value}; OEM power limits unchanged"),
        Rule(@"^GPU 频率偏移已回读确认：核心 (.+) MHz，显存 (.+) MHz。$", m => $"GPU offsets verified: core {m.Groups[1].Value} MHz, memory {m.Groups[2].Value} MHz."),
        Rule(@"^可尝试范围：核心 (.+)～(.+) MHz；显存 (.+)～(.+) MHz。(.*)$", m => $"Available offsets: core {m.Groups[1].Value} to {m.Groups[2].Value} MHz; memory {m.Groups[3].Value} to {m.Groups[4].Value} MHz. {TranslateKnown(m.Groups[5].Value)}"),
        Rule(@"^(.+) · 高级参数只读$", m => TranslateKnown(m.Groups[1].Value) + " · Advanced settings (read-only)")
    ];

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
恢复官方彩色静态键盘背光	Restore the factory multicolor keyboard lighting
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
键盘彩虹 · 平滑流动	Keyboard rainbow · Smooth flow
键盘灯光方案	Keyboard presets
键盘低亮静态 · 深蓝	Dim static keyboard · Deep blue
键盘青蓝呼吸 · 低干扰	Cyan keyboard breathing · Subtle
键盘与海岸灯带自动熄灯（秒级档位）	Keyboard and light-bar sleep timer (seconds)
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
开机后、输入密码前尝试连接上次水冷设备。	Try connecting the last cooling device before Windows sign-in.
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
没有可测画面时显示 FPS —，硬件信息仍会更新。	Shows FPS — when no frames can be measured. Hardware readings still update.
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
睡眠指定时间后进入深度睡眠	Enter deep sleep after the selected time
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
只改变键盘灯光	Changes keyboard lighting only
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
请先添加并选择一个程序；没有目标时显示 FPS —。	Add and select a program. No target is shown as FPS —.
正在启动帧率采集…	Starting frame capture…
显示程序提交的帧率；插帧后的帧数可能不同。	Shows application-presented frames; frame-generation results may differ.
帧率暂不可用，硬件读数不受影响。	FPS is unavailable; hardware readings are unaffected.
目标程序已退出，等待读取新的前台程序。	The target exited. Waiting for a new foreground program.
等待有效帧数据；暂时无法测量时显示 FPS —。	Waiting for frames. Unavailable measurements are shown as FPS —.
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
冲突	Impact
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
""";
}
