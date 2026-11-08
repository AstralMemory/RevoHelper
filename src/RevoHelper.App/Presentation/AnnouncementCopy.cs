namespace RevoHelper.App.Presentation;

/// <summary>
/// Developer-authored, bundled announcement. Edit this file for a release.
/// The page itself downloads nothing; the update check in Updates/ only reads a version number.
/// Bundled copy; publication is managed separately on GitHub.
/// </summary>
public sealed record AnnouncementCopy(
    string Title, string EntryDescription, string VersionLabel, string Version,
    string DateLabel, string UpdatedDate, string NoticeTitle, string Notice,
    string ProjectButton, string DownloadsButton, string FeedbackButton, string BackButton,
    string ReleaseTitle, string Release, string CompatibilityTitle, string Compatibility,
    string UsageTitle, string Usage, string DevelopmentTitle, string Development,
    string FeedbackTitle, string Feedback, string OfflineNote,
    string MirrorButton, string MirrorBackupButton, string MirrorNote)
{
    public const string UpdateDate = "2026-11-08";
    public const string ProjectUrl = "https://github.com/AstralMemory/RevoHelper";
    public const string DownloadsUrl = ProjectUrl + "/releases";
    public const string FeedbackUrl = ProjectUrl + "/issues";

    // Every release also carries a copy named RevoHelper.zip, so this link always means
    // "the newest release" without the app having to reach GitHub first.
    public const string LatestPackageUrl = DownloadsUrl + "/latest/download/RevoHelper.zip";

    // Public relays for users who cannot reach GitHub. They fetch the same file from GitHub;
    // on 2026-11-08 both returned the v1.0.4 package byte for byte (SHA256 dda31d37…).
    // They are opened only when the user clicks; update checks never use them.
    public const string MirrorUrl = "https://ghfast.top/" + LatestPackageUrl;
    public const string MirrorBackupUrl = "https://ghproxy.net/" + LatestPackageUrl;

    // 【禁止修改】开发者公告的内容由产品负责人指定，永久固定。
    // 无论发布什么版本，AI 或开发者都不得改写、替换这两段文字，也不得加入版本号或更新内容。
    // 更新内容写到 GitHub Release 和更新日志。只有产品负责人明确要求时才能改动。
    // DO NOT EDIT: the product owner fixed this wording for every release. Do not rewrite it
    // or add version numbers or release notes. Release notes belong on GitHub and in the changelog.
    public const string DeveloperNoticeChinese = "轻量、免费且开源的机械革命笔记本第三方控制台。在官方控制台的功能基础上拓展更多功能，且性能开销更低、启动更快。";
    public const string DeveloperNoticeEnglish = "RevoHelper is a lightweight, free and open-source third-party control panel for MECHREVO laptops. It extends the functionality of the official control panel, with lower overhead and faster startup.";

    // Only these fixed destinations can be opened by page buttons.
    public static string? LinkFor(string? action) => action switch
    {
        "project" => ProjectUrl,
        "downloads" => DownloadsUrl,
        "feedback" => FeedbackUrl,
        "mirror" => MirrorUrl,
        "mirror-backup" => MirrorBackupUrl,
        _ => null
    };

    public static AnnouncementCopy For(string? language, string version) =>
        LocalizedUiCopy.NormalizeLanguageCode(language) == "en-US"
            ? new("About & announcements", "Version & notes", "Version", version,
                "Updated", UpdateDate, "Product overview",
                DeveloperNoticeEnglish,
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
                "This page is bundled and never fetches announcements. Update checks only read the latest version number from GitHub and can be turned off in Settings. Instructions and licenses come with the release package.",
                "Mirror download", "Backup mirror",
                "Third-party relays of the same GitHub file, opened only on click. Check the SHA256 in the release notes.")
            : new("关于与公告", "版本、更新和使用提醒", "当前版本", version,
                "更新日期", UpdateDate, "产品介绍",
                DeveloperNoticeChinese,
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
                "本页内容随版本提供，不会联网获取公告。检查更新只读取 GitHub 上的最新版本号，不下载任何内容，可在设置中关闭自动检查。使用说明与第三方许可随发布包提供。",
                "国内加速下载", "备用加速线路",
                "加速线路由第三方服务转发 GitHub 上的同一个文件，只在点击时打开。下载后可对照更新日志核对 SHA256。");
}
