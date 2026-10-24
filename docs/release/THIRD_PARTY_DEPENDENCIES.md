# 第三方依赖

以下版本来自主程序、BootWater 的依赖清单、本机还原的 NuGet 包元数据及内嵌组件来源记录。RevoHelper 自有代码采用 MIT；第三方组件各自保留原许可证。根目录和 `docs` 下的原有第三方声明一并保留。

| 组件 | 版本 | 包中声明的许可证 / 来源 |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0；https://github.com/LibreHardwareMonitor/LibreHardwareMonitor |
| BlackSharp.Core | 1.0.7 | MPL-2.0；https://github.com/Blacktempel/BlackSharp |
| DiskInfoToolkit | 1.1.2 | MPL-2.0；https://github.com/Blacktempel/DiskInfoToolkit |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0；https://github.com/Blacktempel/RAMSPDToolkit |
| HidSharp | 2.6.4 | Apache-2.0；原文在 `packaging/ThirdParty/HidSharp-LICENSE.txt` |
| MQTTnet | 5.2.0.1603 | MIT；原文在 `packaging/ThirdParty/MQTTnet-LICENSE.txt` |
| Mono.Posix.NETStandard | 1.0.0 | 包元数据许可链接：https://go.microsoft.com/fwlink/?linkid=869050；项目：https://go.microsoft.com/fwlink/?linkid=869051 |
| System.IO.Ports | 10.0.3 | MIT；https://dot.net/；随附包中的第三方声明 |
| System.Management | 10.0.10 | MIT；https://dot.net/；随附包中的第三方声明 |
| System.CodeDom（BootWater 间接依赖） | 10.0.10 | MIT；https://www.nuget.org/packages/System.CodeDom/10.0.10；随附包中的第三方声明 |
| PawnIO.Modules（由 LibreHardwareMonitorLib 内嵌的 12 个模块） | 0.2.2 | LGPL-2.1-or-later；详细来源见 `THIRD_PARTY_NOTICES.md` |
| PawnIO.Modules IntelMSR / IntelMCHBAR（RevoHelper 另行内嵌） | 0.2.10 | LGPL-2.1-or-later；详细来源见 `THIRD_PARTY_NOTICES.md` |
| PresentMon 独立 x64 控制台采集器 | 2.6.0 | MIT；原文在 `packaging/PresentMon/LICENSE.txt` |

## 分发材料

- 面向用户的运行包将声明、完整许可和 PawnIO 对应源码附件集中到 `Third-Party.txt`；保留两个不同版本的源码 ZIP，不以 0.2.10 替代 0.2.2。
- 开源目录保留 `packaging/ThirdPartySource/PawnIO.Modules-0.2.2-source.zip`、`PawnIO.Modules-0.2.10-source.zip` 两份官方原始源码归档。
- LHM 精确提交的完整上游声明保存在 `packaging/ThirdParty/LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt`，包含 PawnIO LGPL 和 Aga.Controls BSD 原文；保留该原始声明不表示同时分发 LHM 的桌面 GUI。
- System.IO.Ports、System.Management 和 System.CodeDom 的包内第三方声明相同；生成器先核对文本一致，再在输出中合并为一份完整原文。
- 系统的 .NET Desktop Runtime、PawnIO 驱动和可选 RyzenAdj 工具不随本运行包分发；相关外部组件继续遵守各自许可，RevoHelper 不静默安装驱动。

## LHM 内嵌模块版本核对

NuGet 包指向 LHM 源码提交 `3d331e3370efb858411f19511373eff65a218701`。其资源目录历史提交 [4b3ed8b](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/commit/4b3ed8bb4623128927480aaa8cdf0d9e09ebc999) 明确更新到 PawnIO.Modules 0.2.2。核查全部 12 个 DLL 内嵌资源与官方 `release_0_2_2.zip`，逐字节一致。上游资源 README 残留的 0.1.6 文字不作为版本依据。
