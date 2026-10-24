namespace RevoHelper.App.Presentation;

/// <summary>
/// Developer-authored, bundled announcement. Edit this file for a release;
/// opening the page never downloads content or checks for updates.
/// Bundled V1.0.0 copy prepared on 2026-10-25; online publication is separate.
/// </summary>
public sealed record AnnouncementCopy(
    string Title, string EntryDescription, string VersionLabel, string Version,
    string DateLabel, string UpdatedDate, string NoticeTitle, string Notice,
    string ProjectButton, string DownloadsButton, string FeedbackButton, string BackButton,
    string ReleaseTitle, string Release, string CompatibilityTitle, string Compatibility,
    string UsageTitle, string Usage, string DevelopmentTitle, string Development,
    string FeedbackTitle, string Feedback, string OfflineNote)
{
    public const string UpdateDate = "2026-10-25";
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
                "RevoHelper is a lightweight, free third-party control center for MECHREVO laptops. V1.0.0 adds an external performance monitor, dark mode and interface improvements. Check GitHub Releases for publicly available downloads.",
                "Project", "Downloads", "Feedback", "Back to settings",
                "Release & source code",
                "The local V1.0.0 release and source package are prepared for review. RevoHelper's own code uses MIT; third-party components retain their own licenses. This bundled page does not confirm that a release or its source has been published online.",
                "Compatibility",
                "Requires 64-bit Windows 11 and .NET 10 Desktop Runtime. Some models have been verified through developer testing or user feedback; available functions depend on each model. Not every feature on every Intel or AMD laptop has been tested.",
                "Before you use it",
                "RevoHelper can coexist with the official control center without disabling it. OEM fan presets are usually read-only; use Custom to edit curves. Power and clock changes can cause instability. Save your work before changing GPU output and restart manually afterwards.",
                "How it is developed",
                "RevoHelper is a Vibe Coding project, with code mainly written with AI assistance. The developer is responsible for product logic, approving designs and changes, UI design, real-device testing and iteration.",
                "Feedback",
                "Please include your laptop model, CPU/GPU, Windows version, the affected feature, steps and screenshots. Confirmation that a feature works is useful too. Do not upload full system reports containing personal information.",
                "This page is bundled with the app and never fetches announcements in the background. Instructions and third-party licenses are included in the release package.")
            : new("关于与公告", "版本、更新和使用提醒", "当前版本", version,
                "更新日期", UpdateDate, "开发者公告",
                "RevoHelper 是轻量、免费的机械革命第三方控制台。V1.0.0 新增外置性能监控、深色模式并完善界面体验；公开下载请以 GitHub Releases 为准。",
                "GitHub 项目", "下载与更新", "问题反馈", "返回设置",
                "发布与开源",
                "V1.0.0 运行包与源码正在本地准备和验收。自有代码采用 MIT 协议，第三方组件保留各自许可；本页不代表版本或源码已在网上发布，公开进度以 GitHub 为准。",
                "适用范围",
                "需要 Windows 11 64 位及 .NET 10 Desktop Runtime。部分机型已通过开发者实测或用户反馈验证，具体功能以机型实际支持为准；并非所有 Intel、AMD 机型的全部功能都已验证。",
                "使用提醒",
                "可以与官方控制台共存，不会停用或禁用它。官方预设风扇曲线通常只供参考，自定义模式可以调整。调整功耗与频率可能导致不稳定；切换显卡输出前请保存工作，切换后需手动重启。",
                "开发方式声明",
                "本产品属于 Vibe Coding 产物，代码主要由 AI 辅助编写。开发者负责产品逻辑、方案与改动审批、UI 设计、真机测试和迭代改进等工作。",
                "反馈说明",
                "欢迎提供机型、CPU/GPU、Windows 版本、所用功能、复现步骤和截图；确认某项功能正常也很有帮助。请勿上传包含个人信息的完整系统报告。",
                "本页随版本更新，不会后台联网获取公告。使用说明与第三方许可随发布包提供。");
}
