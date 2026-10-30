# RevoHelper

轻量、免费的第三方机械革命笔记本控制台。V1.0.2，开发与维护：Astral Memory。

本仓库当前源码对应 RevoHelper V1.0.2（产品版本 `1.0.2+20261030`，文件版本 `1.0.2.4`）。RevoHelper 自有代码采用 MIT，版权声明为 `Copyright (c) 2026 Astral Memory`，详见 `LICENSE`。第三方组件、嵌入二进制及其对应源码保留各自许可；项目 MIT 不替代它们的许可条件。下载、源码获取与更新以 [项目仓库](https://github.com/AstralMemory/RevoHelper) 为准。

本版的限充适配、会话恢复与源码分发边界见 [V1.0.2 源码说明](./docs/release/V1.0.2_SOURCE_README.md)，用户说明见 [V1.0.2 使用说明](./docs/release/V1.0.2_USER_README.txt)。构建与离线测试不会启用限充，不要为了构建而运行硬件实验。

RevoHelper 采用 Vibe Coding 方式开发，代码主要由 AI 辅助编写。开发者主要负责产品逻辑、方案与改动审批、UI 设计、实际测试和迭代改进等工作。

## 构建

需要 64 位 Windows 和 .NET 10 SDK（`global.json` 指定 10.0.100，允许同主版本较新的功能带）。

```powershell
dotnet restore RevoHelper.sln
dotnet build src/RevoHelper.App/RevoHelper.App.csproj -c Release
dotnet publish src/RevoHelper.App/RevoHelper.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/publish
```

产物为 `artifacts/publish/RevoHelper.exe`，运行仍需要 .NET 10 Desktop Runtime。发行包保留 EXE、使用说明和第三方声明三个文件；不用把编译中间文件一起发布。

## 目录

- `src`：界面、业务模型、Windows / OEM 硬件接口。
- `tests`：单元测试与离线 WPF 界面测试。
- `tools/RevoHelper.BootWater`：可选的登录前水冷组件源码。
- `tools/RevoHelper.Probe`：硬件诊断工具源码，普通用户不需要运行。
- `packaging/BootWater/RevoHelper.BootWater.exe`：由本轮对应源码重建并内嵌的水冷组件，构建主程序时需要保留。
- `packaging/PresentMon`：未修改的独立 FPS 采集器、来源和许可；不是游戏注入组件。
- `packaging/ThirdParty`、`packaging/ThirdPartySource`：许可原文和对应第三方源码材料。
- `docs/release`：使用、构建及重新打包所需的公开文档。
- `Third-Party.txt`：整合的第三方声明、许可原文和对应源码附件。

BootWater 的源码和对应内嵌产物都已保留。主程序的普通构建使用这份已内嵌组件。修改组件时先重新发布它、复制到 `packaging/BootWater`，再重新发布主程序。安装时会把待部署文件与主程序内嵌资源逐字节比对，并检查目录、文件权限和链接；无需手工维护摘要。不保证更换 SDK 或依赖后生成的二进制逐字节相同。

```powershell
dotnet publish tools/RevoHelper.BootWater/RevoHelper.BootWater.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o artifacts/bootwater
Copy-Item artifacts/bootwater/RevoHelper.BootWater.exe packaging/BootWater/RevoHelper.BootWater.exe
```

源码包不执行水冷组件，也不安装任何计划任务。需要登录前连接时，请使用应用内的安装/移除入口；历史直接注册 SYSTEM 任务的脚本不随正式源码分发。

### 本机 OEM 通信边界

RevoHelper 通过固定的本机回环地址与已安装的官方 GCU 服务通信。源码保留该兼容路径所需的固定协议身份；它不是用户个人账号、登录密码、GitHub 密钥或云端服务凭据。协议身份依据开发设备上已安装官方控制中心的通信行为和静态协议检查整理，不包含官方程序或反编译输出；本项目不据此宣称获得厂商授权、存在官方公开 API，或保证所有版本均适用。源码可见也不表示这些固定值应被当作可靠的用户安全凭据。

主程序使用受限的 `Main` 角色（slot 6），BootWater 显式使用独立的 `BootWater` 角色（slot 7），避免登录前组件与主程序同时连接时共用 ClientId。每个角色的客户端身份与认证字段必须配套，连接失败只重试原角色，不回退到主程序身份；不接受任意远程主机或用户提供的角色编号。其他机型若不接受该角色，BootWater 可能无法连接，不能通过回退抢占主程序会话来掩盖失败。修改身份或通道前，应先做不改变设备设置的认证、状态读取和并行会话验证；验证成功仍不等于水冷已实际连接。

## 离线测试

完整测试需要 PowerShell 7（`pwsh`），用于生成并核对第三方声明；普通用户运行程序不需要安装它。

```powershell
dotnet test tests/RevoHelper.Core.Tests/RevoHelper.Core.Tests.csproj
dotnet test tests/RevoHelper.Hardware.Windows.Tests/RevoHelper.Hardware.Windows.Tests.csproj
dotnet test tests/RevoHelper.App.Tests/RevoHelper.App.Tests.csproj
```

测试代码不应被当作真机验证的替代。不要为了构建而启动 Probe 或执行硬件写入命令。

## 重新打包

使用 PowerShell 7 运行打包脚本（`pwsh`）。

```powershell
pwsh -NoProfile -File packaging/Prepare-V102Release.ps1 -PublishExe artifacts/publish/RevoHelper.exe -OutputDirectory artifacts/release-v1.0.2
```

输出目录必须为新目录或空目录，包含运行目录、`RevoHelper v1.0.2.zip`、`RevoHelper v1.0.2 Source Code.zip` 与更新日志。再次打包时使用新的输出路径；脚本不会覆盖已有资源。该步骤只生成本地文件，不操作 GitHub，也不生成哈希。源包需要联网还原 NuGet 依赖，不是全部工具链的离线镜像。历史打包脚本与固定输入仅为兼容既有离线测试而保留，请以本版脚本和说明为准。

## 功能边界

- 部分机型已有实际使用验证或反馈，但仅限已明确的机型、版本和功能；不能将单项好评、相近配置、协议识别或离线测试通过等同于全部硬件功能已验证。其他机型是否支持，应按具体功能分别确认。
- GPU 设置按单字段提交：TGP、Dynamic Boost 开关、Dynamic Boost 瓦数互不捆绑。关闭 Dynamic Boost 只提交关闭开关，保留开启时的瓦数档位，不写 0，也不顺带修改 TGP。
- GPU 滑块拖动期间只更新草稿，松手后提交；隔离过期回读，避免旧操作结果覆盖新草稿或使开关无故跳回。切换模式时界面立即响应，后台完成后再核对；失败时提示并同步已确认状态。
- OEM 确认的配置与驱动当前功耗上限分别报告，不再用旧公式自动补偿 TGP，也不重映射请求目标、保存值或设备回读。驱动上限与配置不同，不应仅凭两者差异就显示为另一个“已设置目标”。
- GPU 调节范围来自机型实时回报。70 W 仅是范围允许时可尝试的目标，不是通用最低值；175 W 等最大值也不应写死为所有机型的上限。是否接受设置及负载下的实际效果需分别确认，不能仅凭滑块范围认定支持，也不保证本机可达到 70 W。
- 官方控制台和 RevoHelper 操作同一硬件，不停止或禁用官方程序及后端服务。
- 官方预设风扇曲线通常只供参考，自定义模式才能调整。
- 主程序不会静默安装驱动。AMD 直连功耗后端使用可选的外部 RyzenAdj，源码包只附说明，不附其可执行文件。
- `artifacts`、`bin`、`obj`、诊断日志、官方反编译材料、个人设置、历史开发记录和旧 Snapshot 包装脚本不包含在此源码包。内部验收记录与公开源码分开保存。

源码清单和第三方许可材料需在每次对外发布前复核。文件检查没有发现个人资料或已收集到许可文件，不等于对所有代码来源、厂商授权或分发合规作绝对保证。
