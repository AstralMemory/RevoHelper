param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$ProductLabel = 'RevoHelper V1.0.0',
    [switch]$IncludePresentMon = $true
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $destination) { throw '第三方声明目标已存在；不会覆盖。' }
$builder = [Text.StringBuilder]::new()

function Add-Section([string]$Title, [string]$RelativePath) {
    $inputPath = Join-Path $projectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $inputPath -PathType Leaf)) { throw "缺少许可输入：$RelativePath" }
    [void]$builder.AppendLine("`r`n================ $Title ================`r`n")
    [void]$builder.AppendLine([IO.File]::ReadAllText($inputPath))
}

[void]$builder.AppendLine("$ProductLabel — Third-Party")
[void]$builder.AppendLine(@'
第三方组件说明、版权与许可证

RevoHelper 自有代码采用 MIT 许可证，Copyright (c) 2026 Astral Memory。
本文件说明随程序分发的第三方组件；这些组件继续遵守各自的许可证，
不因 RevoHelper 采用 MIT 而改为 MIT。本文件不表示任何源码已上传到 GitHub。
普通用户不需要执行本文件中的源码提取命令。

组件及源代码获取地址
1. LibreHardwareMonitorLib 0.9.6 — MPL-2.0
   Copyright LibreHardwareMonitor contributors.
   https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701
   此库还内嵌 PawnIO.Modules 0.2.2（LGPL-2.1-or-later），来源与对应源码见下方。
   上游完整第三方声明（包括 Aga.Controls 的 BSD 声明）原文一并保留。
2. BlackSharp.Core 1.0.7 — MPL-2.0; Copyright Florian K.
   https://github.com/Blacktempel/BlackSharp/tree/c70b735c6cec123ee8a046ac4a0bc6c606f52cf0
3. DiskInfoToolkit 1.1.2 — MPL-2.0; Copyright Florian K.
   https://github.com/Blacktempel/DiskInfoToolkit/tree/25319eae5781e75bcf141e844ceab2afe94d40ea
4. RAMSPDToolkit-NDD 1.4.2 — MPL-2.0; Copyright Florian K.
   https://github.com/Blacktempel/RAMSPDToolkit/tree/3b47b960e0830fef344624ad5e389675d5f0a1ce
   以上为未修改的 NuGet 依赖，MPL 许可全文统一列在下方。
5. HidSharp 2.6.4 — Apache-2.0
   https://github.com/IntergatedCircuits/HidSharp
   https://www.nuget.org/packages/HidSharp/2.6.4
6. MQTTnet 5.2.0.1603 — MIT
   https://github.com/dotnet/MQTTnet
   https://www.nuget.org/packages/MQTTnet/5.2.0.1603
7. Mono.Posix.NETStandard 1.0.0
   Package copyright: Microsoft Corporation.
   包元数据许可地址：https://go.microsoft.com/fwlink/?linkid=869050
   该地址指向 Mono 项目的 LICENSE；其完整说明保留在下方。
   https://github.com/mono/mono
8. System.IO.Ports 10.0.3 / System.Management 10.0.10 / System.CodeDom 10.0.10 — MIT
   https://github.com/dotnet/runtime
   https://www.nuget.org/packages/System.IO.Ports/10.0.3
   https://www.nuget.org/packages/System.Management/10.0.10
   https://www.nuget.org/packages/System.CodeDom/10.0.10
   System.CodeDom 由 BootWater 的 System.Management 依赖引入。
   这三个包的第三方声明完全相同，下方保留一份完整原文。
9. PawnIO.Modules IntelMSR / IntelMCHBAR 0.2.10 — LGPL-2.1-or-later
   Copyright namazso and respective upstream contributors.
   https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.10
   https://github.com/namazso/PawnIO.Modules/tree/0.2.10
   原版 0.2.10 源码 ZIP 作为 Base64 附件保留在本文件末尾。
10. PawnIO.Modules 0.2.2 — LGPL-2.1-or-later（经 LibreHardwareMonitorLib 内嵌）
   Copyright namazso and respective upstream contributors.
   https://github.com/namazso/PawnIO.Modules/releases/tag/0.2.2
   https://github.com/namazso/PawnIO.Modules/tree/0.2.2
   12 个模块：AMDFamily0F、AMDFamily10、AMDFamily17、IntelMSR、IsaBridgeEC、
   LpcACPIEC、LpcCrOSEC、LpcIO、RyzenSMU、SmbusI801、SmbusNCT6793、SmbusPIIX4。
   上游 LHM 更新记录：https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/commit/4b3ed8bb4623128927480aaa8cdf0d9e09ebc999
   已与 0.2.2 官方发行包逐字节核对全部 12 个嵌入模块，内容一致。
   原版 0.2.2 源码 ZIP 另作为 Base64 附件保留在本文件末尾。
   两个附件均为上游已开源的第三方模块源码，不包含 RevoHelper 应用源码。

原始声明及许可原文如下。许可内容不作删节；重复的同一许可合并展示。
'@)

Add-Section 'PawnIO.Modules 原始组件声明' 'THIRD_PARTY_NOTICES.md'
Add-Section 'LibreHardwareMonitor 原始组件声明' 'docs\THIRD-PARTY-NOTICES.md'
Add-Section 'LibreHardwareMonitor 0.9.6 — upstream third-party notices (verbatim)' 'packaging\ThirdParty\LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt'
Add-Section 'Mozilla Public License 2.0' 'packaging\ThirdParty\MPL-2.0.txt'
Add-Section 'HidSharp — Apache License 2.0 and copyright' 'packaging\ThirdParty\HidSharp-LICENSE.txt'
Add-Section 'MQTTnet — MIT License' 'packaging\ThirdParty\MQTTnet-LICENSE.txt'
Add-Section 'Microsoft .NET runtime libraries — MIT License' 'packaging\ThirdParty\Microsoft-MIT.txt'
Add-Section 'Mono project license notice supplied by package license URL' 'packaging\ThirdParty\Mono-Project-LICENSE.txt'
Add-Section 'PawnIO.Modules — GNU Lesser General Public License 2.1' 'packaging\ThirdParty\PawnIO-LGPL-2.1.txt'

$portsNotice = [IO.File]::ReadAllText((Join-Path $projectRoot 'packaging\ThirdParty\System.IO.Ports-THIRD-PARTY-NOTICES.txt'))
$managementNotice = [IO.File]::ReadAllText((Join-Path $projectRoot 'packaging\ThirdParty\System.Management-THIRD-PARTY-NOTICES.txt'))
$codeDomNotice = [IO.File]::ReadAllText((Join-Path $projectRoot 'packaging\ThirdParty\System.CodeDom-THIRD-PARTY-NOTICES.txt'))
# Normalize only line endings and the optional final newline of independently collected originals.
function Normalize-NoticeText([string]$Text) { return $Text.Replace("`r`n", "`n").TrimEnd("`n") }
if ((Normalize-NoticeText $portsNotice) -cne (Normalize-NoticeText $managementNotice) -or
    (Normalize-NoticeText $portsNotice) -cne (Normalize-NoticeText $codeDomNotice)) {
    throw '.NET 包的第三方声明已不同，不能合并省略。'
}
Add-Section 'System.IO.Ports / System.Management / System.CodeDom — shared third-party notices' 'packaging\ThirdParty\System.IO.Ports-THIRD-PARTY-NOTICES.txt'

if ($IncludePresentMon) {
    [void]$builder.AppendLine(@'

================ PresentMon 2.6.0 ================

PresentMon 2.6.0 — MIT
Copyright (C) 2017-2024 Intel Corporation
上游项目：https://github.com/GameTechDev/PresentMon
原版独立采集器：https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe
版本发布：https://github.com/GameTechDev/PresentMon/releases/tag/v2.6.0
对应源码：https://github.com/GameTechDev/PresentMon/tree/v2.6.0
许可原文：https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/LICENSE.txt

RevoHelper 内嵌未修改的独立控制台采集器，不包含上游 GUI、MSI 安装器或常驻服务。
启用性能监控后，按需释放到当前用户的本地缓存，作为受管理的隐藏子进程读取帧事件。
它不向被监测程序注入代码，也不安装服务或驱动；关闭监控后停止采集子进程。
完整的来源记录与 MIT 许可原文如下。
'@)
    Add-Section 'PresentMon 2.6.0 — provenance' 'packaging\PresentMon\README.md'
    Add-Section 'PresentMon 2.6.0 — MIT License' 'packaging\PresentMon\LICENSE.txt'
}

[void]$builder.AppendLine(@'

================ 第三方对应源码附件 ================

附件一：PawnIO.Modules-0.2.2-source.zip（LibreHardwareMonitorLib 内嵌模块）
来源：https://codeload.github.com/namazso/PawnIO.Modules/zip/refs/tags/0.2.2
附件二：PawnIO.Modules-0.2.10-source.zip（RevoHelper 另行内嵌的两个模块）
来源：https://codeload.github.com/namazso/PawnIO.Modules/zip/refs/tags/0.2.10
编码：标准 Base64，解码后即为各自的原始 ZIP；没有修改或执行其中的源码。

需要源码时，可直接使用上方的公开源代码地址，也可以提取这两个附件。
本地提取方法：在 Third-Party.txt 所在目录打开 PowerShell，运行以下命令。
命令只创建两个源码 ZIP；若任一同名文件已存在，则停止，不会覆盖。

$notice = Get-Content -LiteralPath '.\Third-Party.txt' -Raw -Encoding UTF8
$attachments = [regex]::Matches($notice, '(?m)^BEGIN_PAWNIO_SOURCE_BASE64_(?<version>0_2_2|0_2_10)\r?\n(?<payload>[A-Za-z0-9+/=\r\n]+)^END_PAWNIO_SOURCE_BASE64_\k<version>\r?$', [Text.RegularExpressions.RegexOptions]::Multiline)
if ($attachments.Count -ne 2) { throw '找不到两个完整的源码附件' }
$sources = @($attachments | ForEach-Object {
    $version = $_.Groups['version'].Value.Replace('_', '.')
    [pscustomobject]@{
        Path = Join-Path (Get-Location).Path "PawnIO.Modules-$version-source.zip"
        Bytes = [Convert]::FromBase64String($_.Groups['payload'].Value)
    }
})
foreach ($source in $sources) {
    if (Test-Path -LiteralPath $source.Path) { throw "文件已存在，不会覆盖：$($source.Path)" }
}
foreach ($source in $sources) {
    $sourceFile = [IO.File]::Open($source.Path, [IO.FileMode]::CreateNew)
    try { $sourceFile.Write($source.Bytes, 0, $source.Bytes.Length) } finally { $sourceFile.Dispose() }
}

附件开始：
'@)
foreach ($sourceVersion in @('0.2.2', '0.2.10')) {
    $sourceArchive = Join-Path $projectRoot "packaging\ThirdPartySource\PawnIO.Modules-$sourceVersion-source.zip"
    $sourceBase64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($sourceArchive), [Base64FormattingOptions]::InsertLineBreaks)
    $markerVersion = $sourceVersion.Replace('.', '_')
    [void]$builder.AppendLine("BEGIN_PAWNIO_SOURCE_BASE64_$markerVersion")
    [void]$builder.AppendLine($sourceBase64)
    [void]$builder.AppendLine("END_PAWNIO_SOURCE_BASE64_$markerVersion")
}
[IO.File]::WriteAllText($destination, $builder.ToString(), [Text.UTF8Encoding]::new($true))
Get-Item -LiteralPath $destination | Select-Object FullName, Length
