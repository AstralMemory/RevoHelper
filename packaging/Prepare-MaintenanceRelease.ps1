#requires -Version 7.0
param(
    [Parameter(Mandatory = $true)][string]$PublishExe,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishedPath = (Resolve-Path -LiteralPath $PublishExe).Path
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    throw '请指定输出目录。'
}
if (Test-Path -LiteralPath $releaseRoot) {
    $existing = Get-Item -LiteralPath $releaseRoot -Force
    if (-not $existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
        @(Get-ChildItem -LiteralPath $releaseRoot -Force).Count -ne 0) {
        throw '已存在的输出路径必须是空的普通目录。'
    }
}
if ([IO.Path]::GetExtension($publishedPath) -ine '.exe') { throw 'PublishExe 必须是发布后的主 EXE。' }
if ((Get-Item -LiteralPath $publishedPath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'PublishExe 不能是重解析链接。'
}
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($publishedPath)
if ($version.ProductVersion -cne '1.0.1+20261026' -or $version.FileVersion -cne '1.0.1.2') {
    throw "输入版本不符：$($version.ProductVersion) / $($version.FileVersion)"
}

function Source-Entry([string]$RelativePath, [string]$ArchivePath = $RelativePath) {
    $source = Join-Path $projectRoot $RelativePath
    $item = Get-Item -LiteralPath $source
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "打包输入必须是普通文件：$RelativePath"
    }
    [pscustomobject]@{ Source = $item.FullName; Entry = $ArchivePath.Replace('\', '/') }
}

function Copy-NewFile([string]$Source, [string]$Destination) {
    $inputFile = [IO.File]::OpenRead($Source)
    try {
        $outputFile = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
        try { $inputFile.CopyTo($outputFile) } finally { $outputFile.Dispose() }
    } finally { $inputFile.Dispose() }
}

function Write-NewZip([string]$Path, [object[]]$Entries, [string]$Prefix = '') {
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $Entries) {
            if ([IO.Path]::IsPathRooted($entry.Entry) -or $entry.Entry.Split('/') -contains '..' -or
                -not $seen.Add($entry.Entry)) { throw "不安全或重复的打包项：$($entry.Entry)" }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $entry.Source,
                ($Prefix + $entry.Entry), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $archive.Dispose(); $stream.Dispose() }
}

# Explicit public inputs; never copy the development workspace recursively.
$entries = @()
$extensions = @('.cs', '.csproj', '.xaml', '.manifest', '.resx', '.json', '.props', '.targets', '.png', '.ico', '.svg', '.bin')
foreach ($directory in @('src', 'tests', 'tools\RevoHelper.Probe', 'tools\RevoHelper.BootWater')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
        if ($relative -match '(^|\\)(bin|obj|TestResults|\.vs)(\\|$)' -or $file.Extension -notin $extensions) { continue }
        $entries += Source-Entry $relative
    }
}
foreach ($relative in @('.gitignore', 'global.json', 'Directory.Build.props', 'RevoHelper.sln',
    'tools\Generate-AppIcon.ps1', 'tools\ryzenadj\README.md',
    'packaging\BootWater\RevoHelper.BootWater.exe', 'packaging\Build-ThirdPartyNotice.ps1',
    'packaging\Prepare-MaintenanceRelease.ps1',
    'packaging\PresentMon\PresentMon-2.6.0-x64.exe', 'packaging\PresentMon\README.md', 'packaging\PresentMon\LICENSE.txt',
    'THIRD_PARTY_NOTICES.md', 'docs\THIRD-PARTY-NOTICES.md',
    'docs\release\MIT_LICENSE_DRAFT.txt', 'docs\release\THIRD_PARTY_DEPENDENCIES.md',
    'docs\release\V1.0.1.md', 'docs\release\V1.0.1_USER_README.txt', 'docs\release\V1.0.1_SOURCE_README.md')) {
    $entries += Source-Entry $relative
}
foreach ($spec in @(@('packaging\ThirdParty', '*.txt'), @('packaging\ThirdPartySource', '*.zip'))) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $spec[0]) -File -Filter $spec[1]) {
        $entries += Source-Entry ([IO.Path]::GetRelativePath($projectRoot, $file.FullName))
    }
}
$entries += Source-Entry 'docs\release\V1.0.1_SOURCE_README.md' 'README.md'
$entries += Source-Entry 'docs\release\MIT_LICENSE_DRAFT.txt' 'LICENSE'

if (-not (Test-Path -LiteralPath $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}
$runtimeRoot = Join-Path $releaseRoot 'RevoHelper'
New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
Copy-NewFile $publishedPath (Join-Path $runtimeRoot 'RevoHelper.exe')
Copy-NewFile (Join-Path $projectRoot 'docs\release\V1.0.1_USER_README.txt') (Join-Path $runtimeRoot '先阅读我-README.txt')
& (Join-Path $PSScriptRoot 'Build-ThirdPartyNotice.ps1') -OutputPath (Join-Path $runtimeRoot 'Third-Party.txt') -ProductLabel 'RevoHelper V1.0.1' -IncludePresentMon | Out-Null
$runtimeEntries = @('RevoHelper.exe', '先阅读我-README.txt', 'Third-Party.txt') | ForEach-Object {
    [pscustomobject]@{ Source = (Join-Path $runtimeRoot $_); Entry = $_ }
}
Copy-NewFile (Join-Path $projectRoot 'docs\release\V1.0.1.md') (Join-Path $releaseRoot '更新日志-V1.0.1.md')
Write-NewZip (Join-Path $releaseRoot 'RevoHelper-v1.0.1.zip') $runtimeEntries
$entries += [pscustomobject]@{ Source = (Join-Path $runtimeRoot 'Third-Party.txt'); Entry = 'Third-Party.txt' }
Write-NewZip (Join-Path $releaseRoot 'RevoHelper v1.0.1 Source Code.zip') $entries 'RevoHelper v1.0.1 Source Code/'

$runtimeZip = [IO.Compression.ZipFile]::OpenRead((Join-Path $releaseRoot 'RevoHelper-v1.0.1.zip'))
try {
    if ((($runtimeZip.Entries.FullName | Sort-Object) -join '|') -cne
        ((@('RevoHelper.exe', '先阅读我-README.txt', 'Third-Party.txt') | Sort-Object) -join '|')) {
        throw '运行 ZIP 清单不符。'
    }
} finally { $runtimeZip.Dispose() }
Get-ChildItem -LiteralPath $releaseRoot -Recurse -File | Select-Object FullName, Length
