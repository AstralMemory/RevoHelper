#requires -Version 7.0
<#
Local-only V1.0.2 packaging from an already published EXE. Never builds, launches
RevoHelper, invokes historical packagers, accesses hardware/user sessions, changes
startup/security settings or publishes. Outputs are CreateNew; no hashes.
#>
param(
    [Parameter(Mandatory = $true)][string]$PublishExe,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$location = Get-Location
if ($location.Provider.Name -cne 'FileSystem') { throw '请从文件系统目录运行打包。' }
if ([string]::IsNullOrWhiteSpace($PublishExe) -or [string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw '请指定已发布的 EXE 和新的输出目录。'
}
$publishedPath = [IO.Path]::GetFullPath($PublishExe, $location.ProviderPath)
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory, $location.ProviderPath)
$userReadme = 'docs\release\V1.0.2_USER_README.txt'
$sourceReadme = 'docs\release\V1.0.2_SOURCE_README.md'
$changelog = 'docs\release\V1.0.2.md'
$runtimeName = 'RevoHelper v1.0.2.zip'
$sourceName = 'RevoHelper v1.0.2 Source Code.zip'
$sourcePrefix = 'RevoHelper v1.0.2 Source Code/'

function Assert-NoReparsePath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while (-not [string]::IsNullOrEmpty($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "不接受重解析链接或其子路径：$cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -ceq $cursor) { break }
        $cursor = $parent
    }
}
function Assert-EmptyOutput {
    Assert-NoReparsePath $releaseRoot
    foreach ($broad in @([IO.Path]::GetPathRoot($releaseRoot), $projectRoot,
            [IO.Path]::GetDirectoryName($projectRoot),
            [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile),
            [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory))) {
        if ($releaseRoot.TrimEnd('\', '/') -ieq $broad.TrimEnd('\', '/')) {
            throw '输出必须是明确的新子目录，不能是磁盘、工作区、用户或桌面根目录。'
        }
    }
    if (Test-Path -LiteralPath $releaseRoot) {
        $existing = Get-Item -LiteralPath $releaseRoot -Force
        if (-not $existing.PSIsContainer -or @(Get-ChildItem -LiteralPath $releaseRoot -Force).Count -ne 0) {
            throw '输出必须是新目录或空目录；不覆盖、删除或合并已有内容。'
        }
    }
}
function Assert-ArchiveName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or [IO.Path]::IsPathRooted($Name) -or
        $Name.Contains('\') -or $Name.Contains(':') -or $Name.Split('/') -contains '..' -or
        $Name.Split('/') -contains '.' -or $Name.Split('/') -contains '') {
        throw "不安全的压缩条目：$Name"
    }
}
function Assert-PublicSourceName([string]$Name) {
    Assert-ArchiveName $Name
    if ($Name.EndsWith('.bin', [StringComparison]::OrdinalIgnoreCase) -and $Name -cnotin @(
            'src/RevoHelper.Hardware.Windows/Resources/PawnIo/IntelMSR-0.2.10.bin',
            'src/RevoHelper.Hardware.Windows/Resources/PawnIo/IntelMCHBAR-0.2.10.bin')) {
        throw "非公开组件二进制不能进入源码包：$Name"
    }
    if ($Name -match '(?i)(^|/)(bin|obj|TestResults|artifacts|diagnostics|research|firmware|cache|private|\.git|\.codex|AppData)(/|$)' -or
        $Name -match '(?i)(^|/)(lease|status|settings|secrets|credentials)\.json$|(^|/)journal\.jsonl$|(^|/)\.env($|\.)') {
        throw "源码清单包含被禁止的开发或用户资料：$Name"
    }
    if ($Name -match '(?i)(^|/)ExperimentalCharge(/|$)' -and
        $Name -notmatch '^(src/RevoHelper\.Core/Control/ExperimentalCharge/|tests/RevoHelper\.Core\.Tests/Control/ExperimentalCharge/)[^/]+\.cs$') {
        throw "不能收录本机充电会话目录：$Name"
    }
}
function Source-Entry([string]$RelativePath, [string]$ArchivePath = $RelativePath) {
    $source = [IO.Path]::GetFullPath((Join-Path $projectRoot $RelativePath))
    $relative = [IO.Path]::GetRelativePath($projectRoot, $source)
    if ([IO.Path]::IsPathRooted($relative) -or $relative -eq '..' -or $relative.StartsWith('..\')) {
        throw "源码输入不在项目内：$RelativePath"
    }
    Assert-NoReparsePath $source
    $item = Get-Item -LiteralPath $source -Force
    if ($item.PSIsContainer) { throw "需要普通文件：$RelativePath" }
    $name = $ArchivePath.Replace('\', '/')
    Assert-PublicSourceName $name
    [pscustomobject]@{ Source = $item.FullName; Entry = $name }
}
function Public-CodeEntries([string]$RelativeDirectory) {
    $directory = Join-Path $projectRoot $RelativeDirectory
    Assert-NoReparsePath $directory
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force | Sort-Object Name) {
        if ($item.Name -in @('bin', 'obj', 'TestResults', '.vs', '.git')) { continue }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "源码目录含重解析项：$($item.FullName)" }
        $relative = [IO.Path]::GetRelativePath($projectRoot, $item.FullName)
        if ($item.PSIsContainer) { Public-CodeEntries $relative }
        elseif ($item.Extension -in @('.cs', '.csproj', '.xaml', '.manifest', '.resx', '.json', '.props', '.targets', '.png', '.ico', '.svg', '.bin')) {
            Source-Entry $relative
        }
    }
}
function Assert-Entries([object[]]$Entries, [switch]$PublicSource) {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Entries) {
        Assert-ArchiveName $entry.Entry
        if (-not $seen.Add($entry.Entry)) { throw "压缩条目重复：$($entry.Entry)" }
        if ($PublicSource) { Assert-PublicSourceName $entry.Entry }
    }
}
function Copy-NewFile([string]$Source, [string]$Destination) {
    Assert-NoReparsePath $Source; Assert-NoReparsePath $Destination
    $inputFile = [IO.File]::Open($Source, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $outputFile = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
        try { $inputFile.CopyTo($outputFile); $outputFile.Flush($true) } finally { $outputFile.Dispose() }
    } finally { $inputFile.Dispose() }
}
function Write-NewZip([string]$Path, [object[]]$Entries, [string]$Prefix = '') {
    Assert-Entries $Entries; Assert-NoReparsePath $Path
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($entry in $Entries | Sort-Object Entry) {
                $name = $Prefix + $entry.Entry
                Assert-ArchiveName $name; Assert-NoReparsePath $entry.Source
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $entry.Source, $name,
                    [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        } finally { $archive.Dispose() }
        $stream.Flush($true)
    } finally { $stream.Dispose() }
}
function Assert-ZipManifest([string]$Path, [object[]]$Entries, [string]$Prefix = '', [switch]$PublicSource) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $actual = @($archive.Entries | ForEach-Object {
            Assert-ArchiveName $_.FullName
            if (-not $_.FullName.StartsWith($Prefix, [StringComparison]::Ordinal)) { throw '压缩包前缀不符。' }
            if ($PublicSource) { Assert-PublicSourceName $_.FullName.Substring($Prefix.Length) }
            $_.FullName
        } | Sort-Object)
        $expected = @($Entries | ForEach-Object { $Prefix + $_.Entry } | Sort-Object)
        if ($actual.Count -ne $expected.Count -or ($actual -join '|') -cne ($expected -join '|')) {
            throw '压缩包清单不等于明确输入清单。'
        }
    } finally { $archive.Dispose() }
}

# Validate all existing inputs before creating output. No executable is run.
Assert-EmptyOutput
Assert-NoReparsePath $projectRoot; Assert-NoReparsePath $publishedPath
$published = Get-Item -LiteralPath $publishedPath -Force
if ($published.PSIsContainer -or $published.Extension -ine '.exe' -or $published.Length -le 0) {
    throw 'PublishExe 必须是已有的普通主 EXE 文件。'
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($publishedPath)
if ($version.ProductVersion -cne '1.0.2+20261030' -or $version.FileVersion -cne '1.0.2.4') {
    throw "输入版本不符：$($version.ProductVersion) / $($version.FileVersion)"
}

# Only selected product/test/component roots recurse. Never copy general docs/,
# tools/, artifacts/, firmware caches, local notes or user profile directories.
$sourceEntries = @()
foreach ($directory in @('src\RevoHelper.Core', 'src\RevoHelper.Hardware.Windows', 'src\RevoHelper.App',
        'tests\RevoHelper.Core.Tests', 'tests\RevoHelper.Hardware.Windows.Tests', 'tests\RevoHelper.App.Tests',
        'tools\RevoHelper.Probe', 'tools\RevoHelper.BootWater')) {
    $sourceEntries += @(Public-CodeEntries $directory)
}
foreach ($relative in @('.gitignore', 'global.json', 'Directory.Build.props', 'RevoHelper.sln',
        'tools\Generate-AppIcon.ps1', 'tools\ryzenadj\README.md',
        'packaging\BootWater\RevoHelper.BootWater.exe',
        'packaging\Build-ThirdPartyNotice.ps1', 'packaging\Prepare-V102Release.ps1',
        'packaging\PresentMon\PresentMon-2.6.0-x64.exe', 'packaging\PresentMon\README.md', 'packaging\PresentMon\LICENSE.txt',
        'THIRD_PARTY_NOTICES.md', 'docs\THIRD-PARTY-NOTICES.md',
        'docs\release\MIT_LICENSE_DRAFT.txt', 'docs\release\THIRD_PARTY_DEPENDENCIES.md',
        # Existing PUBLIC packaging test fixtures only; never invoked here.
        'packaging\Prepare-MaintenanceRelease.ps1', 'packaging\Prepare-GitHubRelease.ps1',
        'docs\release\SOURCE_README.md', 'docs\release\USER_README.txt',
        'docs\release\GITHUB_RELEASE.md', 'docs\release\GITHUB_REPOSITORY_README.md',
        'docs\release\RELEASE_RESOURCES_README.md',
        'docs\release\V1.0.1.md', 'docs\release\V1.0.1_USER_README.txt', 'docs\release\V1.0.1_SOURCE_README.md',
        $userReadme, $sourceReadme, $changelog)) {
    $sourceEntries += Source-Entry $relative
}
foreach ($name in @('dotnet-LICENSE.txt', 'HidSharp-LICENSE.txt', 'LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt',
        'Microsoft-MIT.txt', 'Mono-Project-LICENSE.txt', 'MPL-2.0.txt', 'MQTTnet-LICENSE.txt', 'PawnIO-LGPL-2.1.txt',
        'System.CodeDom-THIRD-PARTY-NOTICES.txt', 'System.IO.Ports-THIRD-PARTY-NOTICES.txt', 'System.Management-THIRD-PARTY-NOTICES.txt')) {
    $sourceEntries += Source-Entry ('packaging\ThirdParty\' + $name)
}
foreach ($name in @('PawnIO.Modules-0.2.2-source.zip', 'PawnIO.Modules-0.2.10-source.zip')) {
    $sourceEntries += Source-Entry ('packaging\ThirdPartySource\' + $name)
}
$sourceEntries += Source-Entry $sourceReadme 'README.md'
$sourceEntries += Source-Entry 'docs\release\MIT_LICENSE_DRAFT.txt' 'LICENSE'
Assert-Entries $sourceEntries -PublicSource
foreach ($required in @('src/RevoHelper.App/Charging/ChargeSessionEngine.cs',
        'src/RevoHelper.App/Charging/ChargeSessionFiles.cs', 'src/RevoHelper.App/Charging/ChargeRecoveryIntents.cs',
        'src/RevoHelper.App/Charging/ExperimentalChargeClient.cs', 'src/RevoHelper.App/Charging/SessionChargeController.cs',
        'src/RevoHelper.App/Charging/ExperimentalChargeHelperHost.cs',
        'src/RevoHelper.Core/Control/ExperimentalCharge/IExperimentalChargeController.cs',
        'src/RevoHelper.Hardware.Windows/BatteryCharge/ReviewedChargeContracts.cs',
        'src/RevoHelper.Hardware.Windows/BatteryCharge/ReviewedChargeProtocol.cs',
        'src/RevoHelper.Hardware.Windows/BatteryCharge/ReviewedChargeTransport.cs',
        'src/RevoHelper.Hardware.Windows/BatteryCharge/SystemManagementReviewedChargeBackend.cs',
        'src/RevoHelper.Core/Control/LiquidCoolingReconnectCoordinator.cs',
        'src/RevoHelper.App/Presentation/MainWindowViewModel.LiquidCoolingRecovery.cs',
        'src/RevoHelper.App/Presentation/MainWindowViewModel.LiquidCoolingStrategy.cs',
        'tests/RevoHelper.App.Tests/Charging/SessionChargeControllerTests.cs',
        'tests/RevoHelper.App.Tests/Charging/ChargeSessionEngineTests.cs',
        'tests/RevoHelper.App.Tests/Charging/ChargeSessionContractTests.cs',
        'tests/RevoHelper.Hardware.Windows.Tests/BatteryCharge/ReviewedChargeProtocolTests.cs',
        'tests/RevoHelper.Hardware.Windows.Tests/BatteryCharge/ReviewedChargeTransportTests.cs',
        'tests/RevoHelper.Core.Tests/Control/LiquidCoolingReconnectCoordinatorTests.cs')) {
    if ($required -cnotin $sourceEntries.Entry) { throw "源码包缺少本轮必要实现或测试：$required" }
}

Assert-EmptyOutput
if (-not (Test-Path -LiteralPath $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
Assert-NoReparsePath $releaseRoot
$runtimeRoot = Join-Path $releaseRoot 'RevoHelper'
New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
Copy-NewFile $publishedPath (Join-Path $runtimeRoot 'RevoHelper.exe')
Copy-NewFile (Join-Path $projectRoot $userReadme) (Join-Path $runtimeRoot '先阅读我-README.txt')
Copy-NewFile (Join-Path $projectRoot $changelog) (Join-Path $releaseRoot '更新日志-V1.0.2.md')

# Invoke only the reviewed notice builder on our own new staging child.
$noticeDirectory = Join-Path $releaseRoot ('.notice-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $noticeDirectory | Out-Null
$noticeTemporary = Join-Path $noticeDirectory 'Third-Party.txt'
try {
    & (Join-Path $PSScriptRoot 'Build-ThirdPartyNotice.ps1') -OutputPath $noticeTemporary -ProductLabel 'RevoHelper V1.0.2' -IncludePresentMon | Out-Null
    Copy-NewFile $noticeTemporary (Join-Path $runtimeRoot 'Third-Party.txt')
}
finally {
    if ([IO.Path]::GetRelativePath($releaseRoot, $noticeDirectory) -notmatch '^\.notice-[0-9a-f]{32}$') { throw '临时路径边界检查失败。' }
    Assert-NoReparsePath $noticeDirectory
    if ([IO.File]::Exists($noticeTemporary)) { [IO.File]::Delete($noticeTemporary) }
    if ([IO.Directory]::Exists($noticeDirectory)) { [IO.Directory]::Delete($noticeDirectory, $false) }
}
$runtimeEntries = @('RevoHelper.exe', '先阅读我-README.txt', 'Third-Party.txt') | ForEach-Object {
    [pscustomobject]@{ Source = (Join-Path $runtimeRoot $_); Entry = $_ }
}
$sourceEntries += [pscustomobject]@{ Source = (Join-Path $runtimeRoot 'Third-Party.txt'); Entry = 'Third-Party.txt' }
Assert-Entries $sourceEntries -PublicSource
$runtimeZip = Join-Path $releaseRoot $runtimeName
$sourceZip = Join-Path $releaseRoot $sourceName
Write-NewZip $runtimeZip $runtimeEntries
Write-NewZip $sourceZip $sourceEntries $sourcePrefix
Assert-ZipManifest $runtimeZip $runtimeEntries
Assert-ZipManifest $sourceZip $sourceEntries $sourcePrefix -PublicSource
[pscustomobject]@{ RuntimeDirectory = $runtimeRoot; RuntimeZip = $runtimeZip; SourceZip = $sourceZip;
    SourceFileCount = $sourceEntries.Count; Version = $version.ProductVersion; FileVersion = $version.FileVersion; PublishedToGitHub = $false }
