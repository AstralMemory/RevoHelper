namespace RevoHelper.App.Presentation;

/// <summary>
/// Developer-authored, bundled announcement. Edit this file for a release;
/// opening the page never downloads content or checks for updates.
/// Bundled V1.0.2 copy prepared on 2026-10-30; publication is managed separately on GitHub.
/// </summary>
public sealed record AnnouncementCopy(
    string Title, string EntryDescription, string VersionLabel, string Version,
    string DateLabel, string UpdatedDate, string NoticeTitle, string Notice,
    string ProjectButton, string DownloadsButton, string FeedbackButton, string BackButton,
    string ReleaseTitle, string Release, string CompatibilityTitle, string Compatibility,
    string UsageTitle, string Usage, string DevelopmentTitle, string Development,
    string FeedbackTitle, string Feedback, string OfflineNote)
{
    public const string UpdateDate = "2026-10-30";
    public const string ProjectUrl = "https://github.com/AstralMemory/RevoHelper";
    public const string DownloadsUrl = ProjectUrl + "/releases";
    public const string FeedbackUrl = ProjectUrl + "/issues";

    // Only these developer-controlled destinations can be opened by page buttons.
    public static string? LinkFor(string? action) => action switch
    {
        "project" => ProjectUrl,
        "downloads" => DownloadsUrl,
        "feedback" => FeedbackUrl,
        _ => null
    };

    public static AnnouncementCopy For(string? language, string version) =>
        LocalizedUiCopy.NormalizeLanguageCode(language) == "en-US"
            ? new("About & announcements", "Version & notes", "Version", version,
                "Updated", UpdateDate, "Developer announcement",
                "V1.0.2 adds 60% / 80% / 100% charge control, off by default. 60% and 80% stopped charging in tests on MECHREVO YAOSHI (N.1.18MRO24, EC 1.25); shared platform state may affect fans, power and keys. 100% restores native charging. Water cooling retries unexpected disconnects every 3 seconds for 10 minutes. Long-term behavior and a full charge to the cutoff remain unverified.",
                "Project", "Downloads", "Feedback", "Back to settings",
                "Release & source code",
                "RevoHelper's own code uses MIT; third-party components retain their own licenses. See GitHub Releases for publicly available versions. This build does not unlock GPU power limits or certify hardware compatibility.",
                "Compatibility",
                "Requires 64-bit Windows 11 and .NET 10 Desktop Runtime. Some models have been verified through developer testing or user feedback; available functions depend on each model. Not every feature on every Intel or AMD laptop has been tested.",
                "Before you use it",
                "RevoHelper can coexist with the official control center without disabling it. OEM fan presets are usually read-only; use Custom to edit curves. Power and clock changes can cause instability. Save your work before changing GPU output and restart manually afterwards. Experimental limiting may cause firmware to save a threshold automatically. Minimizing or hiding to the tray keeps limiting active. Actual exit attempts restoration. Unplugging or sleeping pauses and restores; reconnecting or waking resumes after safety checks in this process only. Manual off or health faults block auto-resume; not all side effects are guaranteed reversible.",
                "How it is developed",
                "RevoHelper is a Vibe Coding project, with code mainly written with AI assistance. The developer is responsible for product logic, approving designs and changes, UI design, real-device testing and iteration.",
                "Feedback",
                "Please include your laptop model, CPU/GPU, Windows version, the affected feature, steps and screenshots. Confirmation that a feature works is useful too. Do not upload full system reports containing personal information.",
                "This page is bundled with the app and never fetches announcements in the background. Instructions and third-party licenses are included in the release package.")
            : new("关于与公告", "版本、更新和使用提醒", "当前版本", version,
                "更新日期", UpdateDate, "开发者公告",
                "V1.0.2 新增充电上限开关及 60% / 80% / 100% 三档，默认关闭。100% 恢复原生充电，不持续覆盖平台状态。60% 和 80% 已实测停充；限充使用共享平台状态，可能影响风扇、功耗和按键。目前仅通过 MECHREVO YAOSHI、N.1.18MRO24 BIOS、EC 1.25 机型验证。水冷意外断开后，可在10分钟内每3秒尝试重连同一设备。长期行为及完整从低电量充至上限后停充的过程仍尚待验证。",
                "GitHub 项目", "下载与更新", "问题反馈", "返回设置",
                "发布与开源",
                "自有代码采用 MIT 协议，第三方组件保留各自许可。公开版本以 GitHub Releases 为准。本版没有 GPU 功耗解锁，也不代表机型硬件兼容性认证。",
                "适用范围",
                "需要 Windows 11 64 位及 .NET 10 Desktop Runtime。部分机型已通过开发者实测或用户反馈验证，具体功能以机型实际支持为准；并非所有 Intel、AMD 机型的全部功能都已验证。",
                "使用提醒",
                "可以与官方控制台共存，不会停用或禁用它。官方预设风扇曲线通常只供参考，自定义模式可以调整。调整功耗与频率可能导致不稳定；切换显卡输出前请保存工作，切换后需手动重启。实验性限充可能使固件自动保存阈值；最小化或隐藏到托盘继续限充，真正退出时尝试恢复。拔电或睡眠先恢复原值，接电或唤醒经安全确认后仅同一进程继续；手动关闭或健康异常不自动重新启用，不能保证全部副作用都能恢复。",
                "开发方式声明",
                "本产品属于 Vibe Coding 产物，代码主要由 AI 辅助编写。开发者负责产品逻辑、方案与改动审批、UI 设计、真机测试和迭代改进等工作。",
                "反馈说明",
                "欢迎提供机型、CPU/GPU、Windows 版本、所用功能、复现步骤和截图；确认某项功能正常也很有帮助。请勿上传包含个人信息的完整系统报告。",
                "本页随版本更新，不会后台联网获取公告。使用说明与第三方许可随发布包提供。");
}
