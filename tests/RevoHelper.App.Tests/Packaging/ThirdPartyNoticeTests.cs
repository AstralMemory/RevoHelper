namespace RevoHelper.App.Tests.Packaging;

using System.IO.Compression;
using System.Diagnostics;
using System.Text.RegularExpressions;

public sealed class ThirdPartyNoticeTests
{
    [Fact]
    public void FormalNotice_DefaultsToPresentMonAndKeepsLicensesSeparate()
    {
        var script = File.ReadAllText(Locate("packaging", "Build-ThirdPartyNotice.ps1"));

        Assert.Contains("[switch]$IncludePresentMon = $true", script, StringComparison.Ordinal);
        Assert.Contains("RevoHelper 自有代码采用 MIT", script, StringComparison.Ordinal);
        Assert.Contains("2026 Astral Memory", script, StringComparison.Ordinal);
        Assert.Contains("不因 RevoHelper 采用 MIT 而改为 MIT", script, StringComparison.Ordinal);
        Assert.Contains("PresentMon 2.6.0 — MIT License", script, StringComparison.Ordinal);
        Assert.DoesNotContain("本次预览版本中只发布运行包", script, StringComparison.Ordinal);
        Assert.DoesNotContain("正式版源码计划采用 MIT", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_PreservesLhmOriginalBsdAndLgplNotices()
    {
        var original = File.ReadAllText(Locate("packaging", "ThirdParty",
            "LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt"));
        var script = File.ReadAllText(Locate("packaging", "Build-ThirdPartyNotice.ps1"));

        Assert.Contains("License notice for Aga.Controls", original, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2009, Andrey Gliznetsov", original, StringComparison.Ordinal);
        Assert.Contains("License notice for PawnIO.Modules", original, StringComparison.Ordinal);
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", original, StringComparison.Ordinal);
        Assert.Contains("LibreHardwareMonitor-0.9.6-THIRD-PARTY-NOTICES.txt", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.2")]
    [InlineData("0.2.10")]
    public void Notice_SourceAttachmentKeepsEachOfficialSourceVersion(string version)
    {
        var source = Locate("packaging", "ThirdPartySource", $"PawnIO.Modules-{version}-source.zip");
        using var archive = ZipFile.OpenRead(source);
        var prefix = $"PawnIO.Modules-{version}/";
        Assert.NotNull(archive.GetEntry(prefix + "IntelMSR.p"));
        Assert.NotNull(archive.GetEntry(prefix + "COPYING"));
        Assert.NotNull(archive.GetEntry(prefix + ".github/workflows/ci.yml"));

        var script = File.ReadAllText(Locate("packaging", "Build-ThirdPartyNotice.ps1"));
        Assert.Contains("@('0.2.2', '0.2.10')", script, StringComparison.Ordinal);
        Assert.Contains($"PawnIO.Modules-{version}-source.zip", script, StringComparison.Ordinal);
        Assert.Contains("BEGIN_PAWNIO_SOURCE_BASE64_$markerVersion", script, StringComparison.Ordinal);
        Assert.Contains("[IO.FileMode]::CreateNew", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DotnetNotice_CanMergeOnlyIdenticalOriginalNoticeText()
    {
        static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
        var ports = Normalize(File.ReadAllText(Locate("packaging", "ThirdParty", "System.IO.Ports-THIRD-PARTY-NOTICES.txt")));
        var management = Normalize(File.ReadAllText(Locate("packaging", "ThirdParty", "System.Management-THIRD-PARTY-NOTICES.txt")));
        var codeDom = Normalize(File.ReadAllText(Locate("packaging", "ThirdParty", "System.CodeDom-THIRD-PARTY-NOTICES.txt")));
        Assert.Equal(ports, management);
        Assert.Equal(ports, codeDom);
        Assert.Contains(".NET Runtime uses third-party libraries", codeDom, StringComparison.Ordinal);

        var script = File.ReadAllText(Locate("packaging", "Build-ThirdPartyNotice.ps1"));
        Assert.Contains("System.CodeDom 10.0.10", script, StringComparison.Ordinal);
        Assert.Contains("Normalize-NoticeText $codeDomNotice", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedNotice_DefaultIncludesCompleteNoticesAndBothUnchangedSourceArchives()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "RevoHelper-notice-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var output = Path.Combine(temporaryDirectory, "Third-Party.txt");
        var script = Locate("packaging", "Build-ThirdPartyNotice.ps1");
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        // Execute the actual UTF-8 script with PowerShell 7 so $PSScriptRoot remains its packaging directory.
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(script);
        startInfo.ArgumentList.Add("-OutputPath");
        startInfo.ArgumentList.Add(output);
        using var process = new Process { StartInfo = startInfo };
        var started = false;
        try
        {
            started = process.Start();
            Assert.True(started);
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, await stderr + await stdout);

            var notice = await File.ReadAllTextAsync(output);
            Assert.Contains("PresentMon 2.6.0 — MIT License", notice, StringComparison.Ordinal);
            Assert.Contains("Copyright (c) 2009, Andrey Gliznetsov", notice, StringComparison.Ordinal);
            Assert.Contains("System.CodeDom 10.0.10", notice, StringComparison.Ordinal);
            Assert.Contains("RevoHelper 自有代码采用 MIT", notice, StringComparison.Ordinal);
            var attachments = Regex.Matches(notice,
                @"(?m)^BEGIN_PAWNIO_SOURCE_BASE64_(?<version>0_2_2|0_2_10)\r?\n(?<payload>[A-Za-z0-9+/=\r\n]+)^END_PAWNIO_SOURCE_BASE64_\k<version>\r?$");
            Assert.Equal(2, attachments.Count);
            foreach (Match attachment in attachments)
            {
                var version = attachment.Groups["version"].Value.Replace('_', '.');
                var expected = await File.ReadAllBytesAsync(Locate("packaging", "ThirdPartySource", $"PawnIO.Modules-{version}-source.zip"));
                Assert.Equal(expected, Convert.FromBase64String(attachment.Groups["payload"].Value));
            }
        }
        finally
        {
            if (started && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            if (File.Exists(output)) File.Delete(output);
            Directory.Delete(temporaryDirectory);
        }
    }

    private static string Locate(params string[] path)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. path]);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(Path.Combine(path));
    }
}
