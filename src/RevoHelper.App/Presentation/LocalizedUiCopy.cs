namespace RevoHelper.App.Presentation;

public sealed record UiLanguageOption(string Code, string Label);

public sealed class LocalizedUiCopy
{
    public string HomeTitle { get; init; } = "";
    public string OfficeTitle { get; init; } = "";
    public string OfficeDescription { get; init; } = "";
    public string BalancedTitle { get; init; } = "";
    public string BalancedDescription { get; init; } = "";
    public string TurboTitle { get; init; } = "";
    public string TurboDescription { get; init; } = "";
    public string QuietTitle { get; init; } = "";
    public string HighPerformanceTitle { get; init; } = "";
    public string CustomTitle { get; init; } = "";
    public string CustomDescription { get; init; } = "";
    public string CustomPlanHint { get; init; } = "";
    public string ProfileTendencyTitle { get; init; } = "";
    public string MoreProfilesTitle { get; init; } = "";
    public string MoreProfilesHint { get; init; } = "";
    public string NoiseLabel { get; init; } = "";
    public string PerformanceLabel { get; init; } = "";
    public string TemperatureLabel { get; init; } = "";
    public string UsageLabel { get; init; } = "";
    public string PowerUsageLabel { get; init; } = "";
    public string FrequencyLabel { get; init; } = "";
    public string GpuOutputTitle { get; init; } = "";
    public string GpuOutputSubtitle { get; init; } = "";
    public string LightingTitle { get; init; } = "";
    public string LightingSubtitle { get; init; } = "";
    public string LiquidCoolingTitle { get; init; } = "";
    public string LiquidCoolingSubtitle { get; init; } = "";
    public string KeyboardTitle { get; init; } = "";
    public string KeyboardSubtitle { get; init; } = "";
    public string BatteryTitle { get; init; } = "";
    public string BatterySubtitle { get; init; } = "";
    public string DisplayTitle { get; init; } = "";
    public string DisplaySubtitle { get; init; } = "";
    public string OtherTitle { get; init; } = "";
    public string MonitorTitle { get; init; } = "";
    public string MonitorEntryTitle { get; init; } = "";
    public string PerformanceAdjustmentTitle { get; init; } = "";
    public string FanAdjustmentTitle { get; init; } = "";
    public string SettingsTitle { get; init; } = "";
    public string StartupSectionTitle { get; init; } = "";
    public string StartWithWindowsTitle { get; init; } = "";
    public string StartWithWindowsDescription { get; init; } = "";
    public string WindowSectionTitle { get; init; } = "";
    public string WindowScaleTitle { get; init; } = "";
    public string WindowScaleDescription { get; init; } = "";
    public string AlwaysOnTopTitle { get; init; } = "";
    public string AlwaysOnTopDescription { get; init; } = "";
    public string RememberWindowPositionTitle { get; init; } = "";
    public string RememberWindowPositionDescription { get; init; } = "";
    public string InterfaceSectionTitle { get; init; } = "";
    public string LanguageTitle { get; init; } = "";
    public string OpenSourceAndFree { get; init; } = "";
    public string PerformanceDrawerDescription { get; init; } = "";
    public string FanDrawerDescription { get; init; } = "";
    public string GpuOutputDrawerDescription { get; init; } = "";
    public string LightingDrawerDescription { get; init; } = "";
    public string LiquidCoolingDrawerDescription { get; init; } = "";
    public string KeyboardDrawerDescription { get; init; } = "";
    public string BatteryDrawerDescription { get; init; } = "";
    public string ExperimentalChargeTitle { get; init; } = "";
    public string ExperimentalChargeDefault { get; init; } = "";
    public string ExperimentalChargeRisk { get; init; } = "";
    public string ExperimentalChargeEnable { get; init; } = "";
    public string ExperimentalChargeStop { get; init; } = "";
    public string ExperimentalChargeRecover { get; init; } = "";
    public string ExperimentalChargeConfirmTitle { get; init; } = "";
    public string ExperimentalChargeConfirmQuestion { get; init; } = "";
    public string ExperimentalChargeNativeNotice { get; init; } = "";
    public string ExperimentalChargeDelay { get; init; } = "";
    public string ExperimentalChargeNoLimit { get; init; } = "";
    public string ExperimentalChargeConfirmSummary { get; init; } = "";
    public string PowerModeFollowHint { get; init; } = "";
    public string DisplayDrawerDescription { get; init; } = "";
    public string OtherDrawerDescription { get; init; } = "";
    public string SettingsDrawerDescription { get; init; } = "";

    public static string NormalizeLanguageCode(string? code) =>
        string.Equals(code, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "zh-CN";

    public static LocalizedUiCopy For(string? code) =>
        NormalizeLanguageCode(code) == "en-US" ? English : Chinese;

    public static LocalizedUiCopy Chinese { get; } = new()
    {
        HomeTitle = "笔记本性能模式调度",
        OfficeTitle = "办公模式", OfficeDescription = "低噪音与低功耗",
        BalancedTitle = "平衡模式", BalancedDescription = "平衡性能与温度的通用档位",
        TurboTitle = "狂暴模式", TurboDescription = "双策略高性能档",
        QuietTitle = "静音", HighPerformanceTitle = "高能",
        CustomTitle = "自定义模式", CustomDescription = "保存自定义方案", CustomPlanHint = "选择自定义方案",
        ProfileTendencyTitle = "档位倾向参考", MoreProfilesTitle = "更多方案", MoreProfilesHint = "动态节能附加策略",
        NoiseLabel = "噪音", PerformanceLabel = "性能", TemperatureLabel = "温度",
        UsageLabel = "使用率", PowerUsageLabel = "功耗", FrequencyLabel = "频率",
        GpuOutputTitle = "显卡输出模式", GpuOutputSubtitle = "混合 / 独显直连",
        LightingTitle = "灯光调控", LightingSubtitle = "RGB / 按键灯",
        LiquidCoolingTitle = "水冷", LiquidCoolingSubtitle = "连接与控制",
        KeyboardTitle = "按键开关", KeyboardSubtitle = "Win / Fn / OSD",
        BatteryTitle = "电源", BatterySubtitle = "功耗释放模式",
        DisplayTitle = "显示", DisplaySubtitle = "色彩与屏幕功能", OtherTitle = "其它", MonitorTitle = "性能监控（外置监控小横条）", MonitorEntryTitle = "性能监控",
        PerformanceAdjustmentTitle = "性能调整", FanAdjustmentTitle = "风扇调整", SettingsTitle = "设置",
        StartupSectionTitle = "启动", StartWithWindowsTitle = "随 Windows 启动", StartWithWindowsDescription = "登录后驻留系统托盘",
        WindowSectionTitle = "窗口", WindowScaleTitle = "窗口缩放", WindowScaleDescription = "选择后立即生效，默认 100%。",
        AlwaysOnTopTitle = "窗口始终置顶", AlwaysOnTopDescription = "让面板保持在其他窗口上方",
        RememberWindowPositionTitle = "记住窗口位置", RememberWindowPositionDescription = "下次启动时恢复主面板位置",
        InterfaceSectionTitle = "界面", LanguageTitle = "语言", OpenSourceAndFree = "免费使用",
        PerformanceDrawerDescription = "调整当前模式的功耗与频率。",
        FanDrawerDescription = "查看风扇曲线，自定义模式可以调整。",
        GpuOutputDrawerDescription = "切换混合输出或独显直连，重启后生效。",
        LightingDrawerDescription = "设置各区域灯光，也可导入或分享键盘方案。",
        LiquidCoolingDrawerDescription = "连接水冷设备，调整风扇和水泵。",
        KeyboardDrawerDescription = "设置 Win、Fn 键及屏幕提示。",
        BatteryDrawerDescription = "设置电池保护和电源模式。",
        ExperimentalChargeTitle = "充电上限",
        ExperimentalChargeDefault = "默认关闭，仅本次会话启用。",
        ExperimentalChargeRisk = "60% / 80% 限充期间将持续覆盖平台状态，可能影响风扇、功耗和按键；固件可能自动保存限充值。最小化或隐藏到托盘继续限充；真正退出时尝试恢复。拔电或睡眠先恢复原值，接电或唤醒经安全确认后仅同一进程继续限充；手动关闭或健康异常不自动重新启用。不能保证全部副作用都能恢复。100% 不设置限充，按原生策略充电；关闭开关则退出本功能。已在机械革命耀世实测；不按品牌或型号限制，仍需通过接口和原值检查。",
        ExperimentalChargeEnable = "手动启用",
        ExperimentalChargeStop = "停止并恢复",
        ExperimentalChargeRecover = "重试恢复原值",
        ExperimentalChargeConfirmTitle = "启用充电上限？",
        ExperimentalChargeConfirmQuestion = "了解上述说明并启用所选充电上限？",
        ExperimentalChargeNativeNotice = "实验性限充启用或恢复未完成期间，暂停原生养护档位切换。",
        ExperimentalChargeDelay = "切换或应用可能需要15–30秒。",
        ExperimentalChargeNoLimit = "不限制，按原生策略充电",
        ExperimentalChargeConfirmSummary = "60% / 80% 限充会持续覆盖平台状态，可能影响风扇、功耗和按键，固件也可能自动保存限充值。停止时会尝试恢复，但不能保证全部副作用都能恢复。100% 不设置限充，按原生策略充电；关闭开关则退出本功能。",
        PowerModeFollowHint = "办公→最佳能效，平衡/自定义→平衡，狂暴→最佳性能。",
        DisplayDrawerDescription = "设置屏幕颜色、背光和刷新率。",
        OtherDrawerDescription = "定时关机、风扇强冷等工具。",
        SettingsDrawerDescription = "设置开机启动、窗口和语言。"
    };

    public static LocalizedUiCopy English { get; } = new()
    {
        HomeTitle = "Laptop performance profiles",
        OfficeTitle = "Office", OfficeDescription = "Quiet and efficient",
        BalancedTitle = "Balanced", BalancedDescription = "Everyday balance",
        TurboTitle = "Turbo", TurboDescription = "Dual mode",
        QuietTitle = "Quiet", HighPerformanceTitle = "High",
        CustomTitle = "Custom", CustomDescription = "Saved profile", CustomPlanHint = "Select a custom profile",
        ProfileTendencyTitle = "Profile guide", MoreProfilesTitle = "More", MoreProfilesHint = "Additional dynamic power strategies",
        NoiseLabel = "Noise", PerformanceLabel = "Perf.", TemperatureLabel = "Temp.",
        UsageLabel = "Usage", PowerUsageLabel = "Power", FrequencyLabel = "Clock",
        GpuOutputTitle = "GPU mode", GpuOutputSubtitle = "Hybrid / discrete",
        LightingTitle = "Lighting", LightingSubtitle = "RGB / keyboard",
        LiquidCoolingTitle = "Cooling", LiquidCoolingSubtitle = "Connect / control",
        KeyboardTitle = "Keys", KeyboardSubtitle = "Win / Fn / OSD",
        BatteryTitle = "Power", BatterySubtitle = "Power profiles",
        DisplayTitle = "Display", DisplaySubtitle = "Color and screen", OtherTitle = "Other", MonitorTitle = "Performance monitor (floating bar)", MonitorEntryTitle = "Monitor",
        PerformanceAdjustmentTitle = "Performance", FanAdjustmentTitle = "Fan control", SettingsTitle = "Settings",
        StartupSectionTitle = "Startup", StartWithWindowsTitle = "Start with Windows", StartWithWindowsDescription = "Stay available in the system tray after sign-in",
        WindowSectionTitle = "Window", WindowScaleTitle = "Window scale", WindowScaleDescription = "Applies immediately; default is 100%.",
        AlwaysOnTopTitle = "Always on top", AlwaysOnTopDescription = "Keep the panel above other windows",
        RememberWindowPositionTitle = "Remember window position", RememberWindowPositionDescription = "Restore the main panel position next time",
        InterfaceSectionTitle = "Interface", LanguageTitle = "Language", OpenSourceAndFree = "Free to use",
        PerformanceDrawerDescription = "Adjust power and clock settings for the current profile.",
        FanDrawerDescription = "View fan curves. Switch to Custom to edit them.",
        GpuOutputDrawerDescription = "Switch Hybrid or discrete output. Restart to apply.",
        LightingDrawerDescription = "Control keyboard, light bar, logo, and liquid-cooling lighting.",
        LiquidCoolingDrawerDescription = "View and control the supported Bluetooth liquid-cooling device.",
        KeyboardDrawerDescription = "Control Win, Fn, OSD, and other supported hardware switches.",
        BatteryDrawerDescription = "Switch the OEM battery and power profile and verify its current state.",
        ExperimentalChargeTitle = "Charge limit",
        ExperimentalChargeDefault = "Off by default. Enabled for this session only.",
        ExperimentalChargeRisk = "During 60% / 80% charge limiting, this feature continuously overrides the platform state and may affect fans, power and keys. Firmware may automatically save the charge limit. Minimizing or hiding to the tray keeps limiting active. Actual exit attempts restoration. Unplugging or sleeping pauses and restores; reconnecting or waking resumes after safety checks in this process only. Manual off or health faults block auto-resume; not all side effects are guaranteed reversible. 100% sets no charge limit and follows the native charging policy; switching off exits this feature. Tested on MECHREVO YAOSHI; no brand or model restriction, but interface and original-value checks must pass.",
        ExperimentalChargeEnable = "Enable manually",
        ExperimentalChargeStop = "Stop and restore",
        ExperimentalChargeRecover = "Retry restoration",
        ExperimentalChargeConfirmTitle = "Enable charge limit?",
        ExperimentalChargeConfirmQuestion = "Have you read these notes and want to enable the selected charge limit?",
        ExperimentalChargeNativeNotice = "Native battery care profiles are unavailable while experimental limiting is active or restoration is incomplete.",
        ExperimentalChargeDelay = "Switching or applying may take 15–30 seconds.",
        ExperimentalChargeNoLimit = "No limit; charge according to the native policy",
        ExperimentalChargeConfirmSummary = "60% / 80% charge limiting continuously overrides the platform state and may affect fans, power and keys. Firmware may automatically save the charge limit. Stopping attempts restoration, but not all side effects are guaranteed to be reversible. 100% sets no charge limit and follows the native charging policy; switching off exits this feature.",
        PowerModeFollowHint = "Office: best power efficiency; Balanced/Custom: balanced; Turbo: best performance.",
        DisplayDrawerDescription = "Manage color, Mini LED local dimming, and Windows refresh rate.",
        OtherDrawerDescription = "Scheduled shutdown, maximum fan cooling, and other system tools.",
        SettingsDrawerDescription = "Startup, compatibility, interface, and language settings."
    };
}
