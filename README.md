# RevoHelper

轻量、免费且开源的机械革命笔记本第三方控制台。在官方控制台的功能基础上拓展更多功能，且性能开销更低、启动更快。

**当前最新版本：V1.0.2 正式版**

## 产品预览

以下为产品界面预览：

<table>
  <tr>
    <td align="center"><a href="./docs/images/preview-snapshot4-about.png"><img src="./docs/images/preview-snapshot4-about.png" width="410" alt="RevoHelper Snapshot 4 主界面"></a><br>主界面</td>
    <td align="center"><a href="./docs/images/preview-snapshot4-controls.png"><img src="./docs/images/preview-snapshot4-controls.png" width="410" alt="RevoHelper Snapshot 4 性能参数控制界面"></a><br>性能参数控制界面</td>
  </tr>
</table>

## 主要功能与改进（含独有功能）

- **性能调度与功耗调整**：切换办公、平衡、狂暴和自定义模式；在机型允许的范围内，基于官方性能预设调整 CPU 功耗上限、限制 CPU 频率，以及修改 CPU 睿频（Boost）策略。
- **性能监控横条**：通过可置顶的外置小横条显示 FPS，以及 CPU / GPU 的温度、使用率、功耗和频率；不向游戏或程序注入代码，也不限于在游戏中使用。
- **显卡输出与屏幕设置**：优化显卡输出模式切换后的重启交互，并提供更多刷新率档位；具体选项取决于机型与屏幕支持情况。
- **灯光与常用设置**：集中调整支持的键盘、灯带、LOGO 和水冷灯光，不必只使用官方默认预设。
- **实验性充电上限**：在“电源”页底部手动开启，提供 60% / 80% / 100% 三档；100% 先解除已有实验限制，再按笔记本原生策略充电，关闭开关则完全退出本功能。仅对匹配的适配项开放。
- **水冷控制与重连**：提供智慧控制开关与更安静、安静、均衡、最大四档手动策略；已连接设备意外断开后，最多保留 10 分钟的同设备重连机会，尝试间隔至少 3 秒。主动断开、更换设备或注水／重置会取消对应重连。
- **更低的性能开销**：本机观察中，启动时内存占用峰值接近 200 MB，转入后台后约为 70 MB；实际占用随运行状态而变化。
- **其他工具**：支持定时关机、动态节能（动态省电模式）等功能。

## 使用前请留意

- **与官方控制台共存**：两边同时修改同一设置时，以电脑实际生效的结果为准。
- **风扇曲线**：官方的预设通常无法调整风扇曲线，所以仅作参考；需要修改时请使用支持的自定义模式。
- **电池保护与限充**：官方保护模式与“任意指定充电百分比上限”不是同一功能。实验性限充默认关闭，切换或应用可能需要 15–30 秒，不会主动将电池放电至目标值。最小化或隐藏到托盘时继续；拔电或睡眠时暂停并尝试恢复原值，接电或唤醒后仅在同一进程安全确认后继续。手动关闭、真正退出或健康／恢复异常不会自动重新启用。
- **限充适配与副作用**：60% / 80% 目前仅在开发者的 MECHREVO YAOSHI Series（BIOS N.1.18MRO24、EC 1.25）验证有效，不能推广到全部机型与固件。该路径使用共享平台状态，可能连带影响风扇、功耗和按键，固件也可能自行保存设置；不能承诺所有副作用都可恢复。
- **显卡输出切换**：需要重启电脑，重启前请先保存工作；RevoHelper 不会强制或突然重启电脑。
- **参数调整**：功耗与频率受机型限制，超频可能导致不稳定。手动 CPU 频率限制在程序运行期间生效，正常退出时恢复。
- **水冷启动组件**：BootWater 已内置；只有需要登录前连接水冷并主动点击“安装／修复”时，才会在系统目录部署辅助组件，用于开机后自动连接水冷设备。

## 快速开始：下载与运行

1. 前往 [最新正式版](https://github.com/AstralMemory/RevoHelper/releases/latest)，下载运行包 `RevoHelper.v1.0.2.zip`，不要将 `Source Code` 源码包当作运行包。
2. 解压后运行 `RevoHelper.exe`，建议以管理员身份运行。**RevoHelper是绿色免安装的。**
3. 系统需要 **Windows 11 64 位**和 [.NET 10 Desktop Runtime（桌面运行时）](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)。若提示缺少运行库，请选择 **Desktop Runtime**，而不是普通 Runtime。

运行包内的 `先阅读我-README.txt` 提供使用说明，`Third-Party.txt` 集中列出第三方组件信息。每个版本的具体更新内容，请查看对应 Release 的更新日志。

## 开发与开源

开发与维护：[Astral Memory](https://github.com/AstralMemory)。

本项目采用 **Vibe Coding** 开发方式：仅代码主要由Agent（ChatGPT）辅助编写，开发者则完全负责产品逻辑、方案与改动审批、UI 设计、实际测试和迭代维护。

程序承诺永久免费使用。V1.0.2 应用源码已在本仓库公开，自有代码采用 [MIT 许可证](./LICENSE)，第三方组件遵循各自许可证，详见 [第三方声明](./THIRD_PARTY_NOTICES.md)。

开发者可直接浏览 [应用源码](./src)，构建、测试与目录说明见 [BUILDING.md](./BUILDING.md)。

## 反馈与交流

本项目在2026年8月12日左右开始开发，原本一直是打算自用的性质，在2026年10月4日对外发布，直到目前已做了大量迭代改进的工作。欢迎任何人参与反馈与改进。

一部分机型已通过开发者实测或用户反馈验证。目前收到的反馈涉及多种机械革命机型，以游戏本为主，也包括轻薄本，由于机型与固件版本较多，不承诺全型号、全部功能均可使用，具体以机型实际支持情况为准。

遇到问题欢迎通过 [Issues](https://github.com/AstralMemory/RevoHelper/issues)，或前往 [B站开发者主页](https://space.bilibili.com/3707006445684888) @Astral_Memory 直接反馈。

反馈时建议附上：

- 机型与年份、CPU / GPU 型号。
- RevoHelper 版本与 Windows 版本。
- 使用的是哪个功能，预期结果与实际结果。
- 能复现时的操作步骤，必要时附截图。

正常使用的反馈同样有帮助；无需上传包含个人信息的完整系统报告。无论正面反馈或负面反馈都有价值。不过目前处于缺少负面反馈，开发者自己难以确定bug的存在。

---

## English

RevoHelper is a lightweight, free and open-source third-party control panel for MECHREVO laptops. It extends the functionality of the official control panel, with lower overhead and faster startup.

**Latest version: V1.0.2 — Stable Release**

### Product preview

The following images preview the application interface (The UI text in the image example is in Chinese. You can switch it to English in the settings.):

<table>
  <tr>
    <td align="center"><a href="./docs/images/preview-snapshot4-about.png"><img src="./docs/images/preview-snapshot4-about.png" width="410" alt="RevoHelper Snapshot 4 main interface"></a><br>Main interface</td>
    <td align="center"><a href="./docs/images/preview-snapshot4-controls.png"><img src="./docs/images/preview-snapshot4-controls.png" width="410" alt="RevoHelper Snapshot 4 performance controls"></a><br>Performance controls</td>
  </tr>
</table>

### Main features and improvements (including exclusive features)

- **Performance scheduling and power adjustments:** Switch between Office, Balanced, Turbo and Custom modes. Within the limits supported by the laptop, adjust CPU power limits, limit CPU frequency and change processor Boost policies based on the official performance presets.
- **Performance monitoring bar:** Display FPS and CPU / GPU temperatures, utilization, power and clock speeds in an external bar that can stay on top. It does not inject code into games or applications and is not limited to use in games.
- **GPU output and display settings:** Improved restart handling after switching GPU output modes, with more refresh-rate options. Available options depend on the laptop and display.
- **Lighting and common settings:** Adjust supported keyboard, light-bar, logo and liquid-cooler lighting in one place, without being limited to the official default presets.
- **Experimental charge limits:** Enable the feature manually at the bottom of the Power page and choose 60%, 80% or 100%. 100% first removes any existing experimental limit, then follows the laptop's native charging policy; turning the switch off exits the feature completely. Available only for matching supported configurations.
- **Liquid-cooling control and reconnection:** A Smart Control switch and four manual strategies—Quieter, Quiet, Balanced and Maximum—are available. After an unexpected disconnect, reconnection to the same previously connected device is attempted for up to 10 minutes, at intervals of at least 3 seconds. Manual disconnection, changing devices, priming or resetting cancels the corresponding reconnection attempt.
- **Lower overhead:** On the developer's system, memory usage peaks at nearly 200 MB during startup and drops to around 70 MB in the background. Actual usage varies with operating conditions.
- **Other tools:** Scheduled shutdown, dynamic power saving and other features.

### Before you use RevoHelper

- **Coexistence with the official control panel:** If both applications change the same setting, the result that actually takes effect on the laptop is what matters.
- **Fan curves:** Fan curves in the official preset modes usually cannot be adjusted and are therefore shown for reference only. Use a supported Custom mode to modify them.
- **Battery protection and charge limiting:** Official protection modes and setting an arbitrary charging-percentage limit are different features. Experimental charge limiting is off by default; switching or applying may take 15–30 seconds, and it does not actively discharge the battery to the target. Limiting continues while minimized or hidden to the tray. Unplugging or sleeping pauses control and attempts to restore the original values; reconnecting or waking resumes only after safety checks within the same process. Manual off, actual exit, health faults or unconfirmed restoration prevent automatic resumption.
- **Charge-limit compatibility and side effects:** The 60%/80% settings have currently been verified only on the developer's MECHREVO YAOSHI Series with BIOS N.1.18MRO24 and EC 1.25; this does not establish support for all models or firmware. This path shares platform state and may affect fans, power and keys. Firmware may also save settings automatically; restoration of every side effect cannot be guaranteed.
- **GPU output switching:** A restart is required. Save your work before restarting; RevoHelper will not force or unexpectedly trigger a restart.
- **Parameter adjustments:** Power and frequency settings are subject to the laptop's limits. Overclocking can cause instability. Manual CPU frequency limits apply while the application is running and are restored on normal exit.
- **Liquid-cooling startup helper:** BootWater is bundled. The helper is deployed to the system directory only when you need to connect the cooler before sign-in and explicitly click “Install / Repair.” It is used to connect the liquid-cooling device automatically after startup.

### Quick start: download and run

1. Go to the [latest stable release](https://github.com/AstralMemory/RevoHelper/releases/latest) and download the application package, `RevoHelper.v1.0.2.zip`. Do not use the `Source Code` archive as the application package.
2. Extract the archive and run `RevoHelper.exe`. Running as administrator is recommended. **RevoHelper is portable and requires no installation.**
3. You need **64-bit Windows 11** and the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). If prompted to install a missing runtime, choose **Desktop Runtime**, not the standard Runtime.

The package includes `先阅读我-README.txt` for usage instructions and `Third-Party.txt` for third-party component information. See the changelog in each Release for the changes in that version.

### Development and open source

Developed and maintained by [Astral Memory](https://github.com/AstralMemory).

The project uses a **Vibe Coding** development workflow: code is primarily written with assistance from an agent (ChatGPT), while the developer takes full responsibility for product logic, proposals and change approval, UI design, hands-on testing and ongoing maintenance.

The program is committed to remaining free permanently. The V1.0.2 application source code is available in this repository. The project's own code uses the [MIT License](./LICENSE); third-party components retain their respective licenses. See the [third-party notices](./THIRD_PARTY_NOTICES.md).

Developers can browse the [application source](./src). See [BUILDING.md](./BUILDING.md) for build and test instructions and a guide to the directory structure.

### Feedback and discussion

Development began around August 12, 2026, originally as a project for personal use. It was released publicly on October 4, 2026, and has since been open-sourced and extensively improved through successive iterations. Everyone is welcome to provide feedback and contribute improvements.

Some models have been verified through developer testing or user feedback. Feedback received so far covers various MECHREVO models, mainly gaming laptops but also thin-and-light laptops. With so many models and firmware versions, support for every model and every feature cannot be guaranteed; actual support depends on the laptop.

Report issues through [GitHub Issues](https://github.com/AstralMemory/RevoHelper/issues), or contact the developer directly through the [Bilibili profile](https://space.bilibili.com/3707006445684888) @Astral_Memory.

When providing feedback, please include:

- Laptop model and year, CPU and GPU.
- RevoHelper version and Windows version.
- The feature used, expected behavior and actual result.
- Steps to reproduce the issue, with screenshots if needed.

Reports of normal operation are helpful too; there is no need to upload complete system reports containing personal information. Both positive and negative feedback are valuable. However, negative feedback is currently scarce, making it difficult for the developer alone to identify existing bugs.
