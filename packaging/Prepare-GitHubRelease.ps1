param(
    [Parameter(Mandatory = $true)][string]$PublishExe,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$ResourceDirectoryName = '正式版发布资源'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$releaseRoot = [IO.Path]::GetFullPath($OutputDirectory)
$publishedPath = (Resolve-Path -LiteralPath $PublishExe).Path
if ([IO.Path]::GetExtension($publishedPath) -ne '.exe') { throw 'PublishExe 必须是已发布的主 EXE。' }
if (Test-Path -LiteralPath $releaseRoot -PathType Leaf) { throw 'OutputDirectory 必须是目录。' }
if ([string]::IsNullOrWhiteSpace($ResourceDirectoryName) -or
    $ResourceDirectoryName -in @('.', '..') -or
    $ResourceDirectoryName.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
    [IO.Path]::GetFileName($ResourceDirectoryName) -cne $ResourceDirectoryName) {
    throw 'ResourceDirectoryName 必须是单个新的子目录名，不能包含路径。'
}
$resourceRoot = [IO.Path]::GetFullPath((Join-Path $releaseRoot $ResourceDirectoryName))
if ([IO.Path]::GetRelativePath($releaseRoot, $resourceRoot) -cne $ResourceDirectoryName) {
    throw '发布资源子目录必须位于指定输出目录内。'
}
if (Test-Path -LiteralPath $resourceRoot) { throw '发布资源子目录已存在；请指定新的 ResourceDirectoryName，不覆盖已有文件。' }

function Source-Entry([string]$RelativePath, [string]$ArchivePath = $RelativePath) {
    $sourcePath = Join-Path $projectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "缺少打包输入：$RelativePath" }
    if ((Get-Item -LiteralPath $sourcePath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        throw "打包输入不能是重解析链接：$RelativePath"
    }
    [pscustomobject]@{ Source = $sourcePath; Entry = $ArchivePath.Replace('\', '/') }
}

function Assert-EntryNames([object[]]$Entries) {
    $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in $Entries) {
        if ([IO.Path]::IsPathRooted($item.Entry) -or $item.Entry.Split('/') -contains '..' -or
            -not $names.Add($item.Entry)) { throw "不安全或重复的打包项：$($item.Entry)" }
    }
}

function Copy-NewFile([string]$Source, [string]$Destination) {
    $inputFile = [IO.File]::OpenRead($Source)
    try {
        $outputFile = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
        try { $inputFile.CopyTo($outputFile) } finally { $outputFile.Dispose() }
    }
    finally { $inputFile.Dispose() }
}

function Write-ReleaseZip([string]$Path, [object[]]$Entries) {
    Assert-EntryNames $Entries
    $stream = [IO.File]::Open($Path, [IO.FileMode]::CreateNew)
    $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $Entries) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $item.Source, $item.Entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose(); $stream.Dispose() }
}

# This is a release allowlist, not a recursive copy of the development workspace.
# Legacy Snapshot helpers and direct SYSTEM task registration scripts are not inputs.
$sourceEntries = @()
$sourceExtensions = @('.cs', '.csproj', '.xaml', '.manifest', '.resx', '.json', '.props', '.targets', '.png', '.ico', '.svg', '.bin')
foreach ($directory in @('src', 'tests', 'tools\RevoHelper.Probe', 'tools\RevoHelper.BootWater')) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $directory) -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($projectRoot, $file.FullName)
        if ($relative -match '(^|\\)(bin|obj|TestResults|\.vs)(\\|$)' -or
            $file.Extension -notin $sourceExtensions) { continue }
        $sourceEntries += Source-Entry $relative
    }
}
foreach ($relative in @('.gitignore', 'global.json', 'Directory.Build.props', 'RevoHelper.sln',
    'tools\Generate-AppIcon.ps1', 'tools\ryzenadj\README.md',
    'packaging\BootWater\RevoHelper.BootWater.exe',
    'packaging\Build-ThirdPartyNotice.ps1', 'packaging\Prepare-GitHubRelease.ps1',
    'packaging\PresentMon\PresentMon-2.6.0-x64.exe', 'packaging\PresentMon\README.md',
    'packaging\PresentMon\LICENSE.txt', 'THIRD_PARTY_NOTICES.md', 'docs\THIRD-PARTY-NOTICES.md',
    'docs\release\SOURCE_README.md', 'docs\release\USER_README.txt',
    'docs\release\GITHUB_RELEASE.md', 'docs\release\GITHUB_REPOSITORY_README.md',
    'docs\release\RELEASE_RESOURCES_README.md', 'docs\release\MIT_LICENSE_DRAFT.txt',
    'docs\release\THIRD_PARTY_DEPENDENCIES.md')) {
    $sourceEntries += Source-Entry $relative
}
foreach ($pattern in @(
    @{ Directory = 'packaging\ThirdParty'; Filter = '*.txt' },
    @{ Directory = 'packaging\ThirdPartySource'; Filter = '*.zip' })) {
    foreach ($file in Get-ChildItem -LiteralPath (Join-Path $projectRoot $pattern.Directory) -File -Filter $pattern.Filter) {
        $sourceEntries += Source-Entry ([IO.Path]::GetRelativePath($projectRoot, $file.FullName))
    }
}
$sourceEntries += Source-Entry 'docs\release\SOURCE_README.md' 'README.md'
$sourceEntries += Source-Entry 'docs\release\MIT_LICENSE_DRAFT.txt' 'LICENSE'
Assert-EntryNames $sourceEntries

# The caller may keep edited logs and plans in OutputDirectory. Only this new child is written.
if (-not (Test-Path -LiteralPath $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
New-Item -ItemType Directory -Path $resourceRoot | Out-Null
$runtimeRoot = Join-Path $resourceRoot 'RevoHelper'
$sourceRoot = Join-Path $resourceRoot 'RevoHelper-source'
New-Item -ItemType Directory -Path $runtimeRoot, $sourceRoot | Out-Null
Copy-NewFile $publishedPath (Join-Path $runtimeRoot 'RevoHelper.exe')
Copy-NewFile (Join-Path $projectRoot 'docs\release\USER_README.txt') (Join-Path $runtimeRoot '先阅读我-README.txt')
& (Join-Path $PSScriptRoot 'Build-ThirdPartyNotice.ps1') -OutputPath (Join-Path $runtimeRoot 'Third-Party.txt') -ProductLabel 'RevoHelper V1.0.0' -IncludePresentMon
$runtimeEntries = @(
    [pscustomobject]@{ Source = (Join-Path $runtimeRoot 'RevoHelper.exe'); Entry = 'RevoHelper.exe' },
    [pscustomobject]@{ Source = (Join-Path $runtimeRoot '先阅读我-README.txt'); Entry = '先阅读我-README.txt' },
    [pscustomobject]@{ Source = (Join-Path $runtimeRoot 'Third-Party.txt'); Entry = 'Third-Party.txt' }
)
$sourceEntries += [pscustomobject]@{ Source = (Join-Path $runtimeRoot 'Third-Party.txt'); Entry = 'Third-Party.txt' }
Assert-EntryNames $sourceEntries
foreach ($item in $sourceEntries) {
    $target = Join-Path $sourceRoot $item.Entry
    $parent = [IO.Path]::GetDirectoryName($target)
    if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent | Out-Null }
    Copy-NewFile $item.Source $target
}
Copy-NewFile (Join-Path $projectRoot 'docs\release\RELEASE_RESOURCES_README.md') (Join-Path $resourceRoot '先看这里.md')
Copy-NewFile (Join-Path $projectRoot 'docs\release\GITHUB_RELEASE.md') (Join-Path $resourceRoot 'GitHub发布文案.md')
Copy-NewFile (Join-Path $projectRoot 'docs\release\GITHUB_REPOSITORY_README.md') (Join-Path $resourceRoot 'GitHub主页README.md')
$internalChecklist = Join-Path $projectRoot 'docs\release\SOURCE_RELEASE_CHECKLIST.md'
if (Test-Path -LiteralPath $internalChecklist -PathType Leaf) {
    Copy-NewFile $internalChecklist (Join-Path $resourceRoot '内部发布检查.md')
}
Write-ReleaseZip (Join-Path $resourceRoot 'RevoHelper-v1.0.0.zip') $runtimeEntries
Write-ReleaseZip (Join-Path $resourceRoot 'RevoHelper-v1.0.0-source.zip') $sourceEntries
Get-ChildItem -LiteralPath $resourceRoot -Recurse -File | Select-Object FullName, Length
