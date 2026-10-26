namespace RevoHelper.App.Tests.Packaging;

public sealed class ReleaseSourcePackagingTests
{
    [Fact]
    public void MaintenanceRelease_UsesStableVersionAndDoesNotBundleOldHomepageDrafts()
    {
        var script = Read("packaging", "Prepare-MaintenanceRelease.ps1");
        Assert.Contains("'1.0.1+20261026'", script, StringComparison.Ordinal);
        Assert.Contains("'1.0.1.2'", script, StringComparison.Ordinal);
        Assert.Contains("RevoHelper v1.0.1 Source Code.zip", script, StringComparison.Ordinal);
        Assert.Contains("'docs\\release\\V1.0.1_SOURCE_README.md' 'README.md'", script, StringComparison.Ordinal);
        Assert.Contains("'docs\\release\\MIT_LICENSE_DRAFT.txt' 'LICENSE'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GITHUB_REPOSITORY_README.md", script, StringComparison.Ordinal);
        Assert.DoesNotContain("GITHUB_RELEASE.md", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Prepare-GitHubRelease.ps1", script, StringComparison.Ordinal);
        Assert.Contains("-not $existing.PSIsContainer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void V101SourceReadme_ExplainsStablePackageAndEmptyOutputDirectory()
    {
        var source = Read("docs", "release", "V1.0.1_SOURCE_README.md");
        Assert.Contains("RevoHelper V1.0.1 Source Code", source, StringComparison.Ordinal);
        Assert.Contains("新目录或空目录", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SOURCE_README.md` 的通用部分", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StableRelease_UsesProtectedNewChildrenAndThreeRuntimeFiles()
    {
        var script = Read("packaging", "Prepare-GitHubRelease.ps1");
        Assert.Contains("$ResourceDirectoryName", script, StringComparison.Ordinal);
        Assert.Contains("if (Test-Path -LiteralPath $resourceRoot)", script, StringComparison.Ordinal);
        Assert.Contains("[IO.FileMode]::CreateNew", script, StringComparison.Ordinal);
        Assert.Contains("RevoHelper-v1.0.0.zip", script, StringComparison.Ordinal);
        Assert.Contains("RevoHelper-v1.0.0-source.zip", script, StringComparison.Ordinal);
        Assert.Contains("-IncludePresentMon", script, StringComparison.Ordinal);
        Assert.Contains("Entry = 'RevoHelper.exe'", script, StringComparison.Ordinal);
        Assert.Contains("Entry = '先阅读我-README.txt'", script, StringComparison.Ordinal);
        Assert.Contains("Entry = 'Third-Party.txt'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Force", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-Beta.zip", script, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceAllowlist_IncludesRepackInputsButNotDevelopmentNotesOrLegacyInstallers()
    {
        var script = Read("packaging", "Prepare-GitHubRelease.ps1");
        foreach (var required in new[]
        {
            "'src'", "'tests'", "tools\\RevoHelper.Probe", "tools\\RevoHelper.BootWater",
            "global.json", "Directory.Build.props", "RevoHelper.sln", "tools\\Generate-AppIcon.ps1",
            "docs\\release\\SOURCE_README.md", "docs\\release\\USER_README.txt",
            "docs\\release\\GITHUB_RELEASE.md", "docs\\release\\GITHUB_REPOSITORY_README.md",
            "docs\\release\\RELEASE_RESOURCES_README.md", "docs\\release\\MIT_LICENSE_DRAFT.txt",
            "docs\\release\\THIRD_PARTY_DEPENDENCIES.md", "packaging\\ThirdPartySource",
            "packaging\\PresentMon\\PresentMon-2.6.0-x64.exe", "packaging\\BootWater\\RevoHelper.BootWater.exe"
        }) Assert.Contains(required, script, StringComparison.Ordinal);
        foreach (var excluded in new[]
        {
            "DISTRIBUTION_README.txt", "DEVELOPMENT_LOG", "SNAPSHOT_README", "SNAPSHOT2_README",
            "Prepare-SnapshotRelease.ps1", "Install-BootWaterAutoConnect.ps1", "Uninstall-BootWaterAutoConnect.ps1"
        }) Assert.DoesNotContain(excluded, script, StringComparison.Ordinal);
        Assert.DoesNotContain("$sourceEntries += Source-Entry 'docs\\release\\SOURCE_RELEASE_CHECKLIST.md'", script, StringComparison.Ordinal);
        Assert.Contains("Get-ChildItem -LiteralPath (Join-Path $projectRoot $pattern.Directory) -File -Filter $pattern.Filter", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StableSourceInstructions_DescribePublished100AndRetainBuildAndLicenseDetails()
    {
        var source = Read("docs", "release", "SOURCE_README.md");
        Assert.Contains("本源码包对应 RevoHelper V1.0.0", source, StringComparison.Ordinal);
        Assert.Contains("下载、源码获取与更新以", source, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Astral Memory", source, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/RevoHelper.Core.Tests", source, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/RevoHelper.Hardware.Windows.Tests", source, StringComparison.Ordinal);
        Assert.Contains("dotnet test tests/RevoHelper.App.Tests", source, StringComparison.Ordinal);
        Assert.Contains("packaging/Prepare-GitHubRelease.ps1", source, StringComparison.Ordinal);
        Assert.Contains("逐字节比对", source, StringComparison.Ordinal);
        var license = Read("docs", "release", "MIT_LICENSE_DRAFT.txt");
        Assert.StartsWith("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Astral Memory", license, StringComparison.Ordinal);
    }

    private static string Read(params string[] relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. relativePath]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(Path.Combine(relativePath));
    }
}
