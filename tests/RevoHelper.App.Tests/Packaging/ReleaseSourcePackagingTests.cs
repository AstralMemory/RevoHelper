namespace RevoHelper.App.Tests.Packaging;

public sealed class ReleaseSourcePackagingTests
{
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
    public void SourceAndRuntimeInstructions_DoNotClaimLocalPreparationIsOnlinePublication()
    {
        var source = Read("docs", "release", "SOURCE_README.md");
        Assert.Contains("本地正式版候选源码", source, StringComparison.Ordinal);
        Assert.Contains("不表示应用或源码已经在 GitHub 公开", source, StringComparison.Ordinal);
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
