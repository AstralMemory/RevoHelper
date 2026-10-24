namespace RevoHelper.App.Tests.Packaging;

using System.Xml.Linq;
using RevoHelper.App.Settings;

public sealed class BootWaterPackagingTests
{
    [Fact]
    public void BootHelper_IsShortLivedAndOnlyConnectsRememberedLiquidCoolingDevice()
    {
        var source = File.ReadAllText(Locate("tools", "RevoHelper.BootWater", "Program.cs"));

        Assert.Contains("GcuMqttStatusSource", source, StringComparison.Ordinal);
        Assert.Contains("frontendRole: GcuFrontendRole.BootWater", source, StringComparison.Ordinal);
        Assert.Contains("LiquidCoolingAutoConnector", source, StringComparison.Ordinal);
        Assert.Contains("CancelAfter", source, StringComparison.Ordinal);
        Assert.Contains("CommonApplicationData", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ScanLiquidCoolingDevicesAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectLiquidCoolingDeviceAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartLiquidCoolingPrimingAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ResetLiquidCoolingDeviceAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BootTaskDefinition_IsExplicitShortLivedAndLegacyScriptsAreNotReleaseInputs()
    {
        var xml = BootWaterTaskDefinition.Create(@"C:\ProgramData\RevoHelper\BootWater\RevoHelper.BootWater.exe");
        Assert.Contains("<BootTrigger>", xml, StringComparison.Ordinal);
        Assert.Contains("S-1-5-18", xml, StringComparison.Ordinal);
        Assert.Contains("HighestAvailable", xml, StringComparison.Ordinal);
        Assert.Contains("<ExecutionTimeLimit>PT45S</ExecutionTimeLimit>", xml, StringComparison.Ordinal);
        Assert.Contains("<Arguments>--boot</Arguments>", xml, StringComparison.Ordinal);
        Assert.True(BootWaterTaskDefinition.IsOwned(xml, @"C:\ProgramData\RevoHelper\BootWater\RevoHelper.BootWater.exe"));
        Assert.True(BootWaterTaskDefinition.IsEnabledAndConfigured(xml));

        var release = File.ReadAllText(Locate("packaging", "Prepare-GitHubRelease.ps1"));
        Assert.DoesNotContain("Install-BootWaterAutoConnect.ps1", release, StringComparison.Ordinal);
        Assert.DoesNotContain("Uninstall-BootWaterAutoConnect.ps1", release, StringComparison.Ordinal);
        Assert.DoesNotContain("Prepare-SnapshotRelease.ps1", release, StringComparison.Ordinal);
    }

    [Fact]
    public void InAppInstaller_VerifiesAgainstTheCurrentPackagedHelper()
    {
        var source = File.ReadAllText(Locate("src", "RevoHelper.App", "Settings", "WindowsBootWaterTaskPlatform.cs"));
        Assert.Contains("MatchesPackagedHelper", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HelperSha256", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("modified", false)]
    [InlineData("truncated", false)]
    [InlineData("appended", false)]
    public void InstallerAcceptsOnlyExactEmbeddedHelperContent(string variant, bool expected)
    {
        using var original = WindowsBootWaterTaskPlatform.OpenPackagedHelper();
        using var memory = new MemoryStream();
        original.CopyTo(memory);
        var bytes = memory.ToArray();
        switch (variant)
        {
            case "modified": bytes[^1] ^= 1; break;
            case "truncated": bytes = bytes[..^1]; break;
            case "appended": bytes = [.. bytes, 0]; break;
        }
        using var candidate = new MemoryStream(bytes, writable: false);
        Assert.Equal(expected, WindowsBootWaterTaskPlatform.MatchesPackagedHelper(candidate));
    }

    [Fact]
    public void MainExecutable_ContainsBootHelperWithoutAdjacentFiles()
    {
        using var stream = WindowsBootWaterTaskPlatform.OpenPackagedHelper();
        Assert.True(stream.Length > 1000000);
        Assert.Equal('M', stream.ReadByte());
        Assert.Equal('Z', stream.ReadByte());
        var source = File.ReadAllText(Locate("src", "RevoHelper.App", "Settings", "WindowsBootWaterTaskPlatform.cs"));
        Assert.DoesNotContain("AppContext.BaseDirectory", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WaterCoolingDrawer_PresentsPreLoginInstallAsSupportedBetaFeature()
    {
        var xaml = File.ReadAllText(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        Assert.Contains("登录前自动连接", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("登录前自动连接 · 实验", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void BootHelper_BundlesNativeDependenciesInsideItsSingleExecutable()
    {
        var project = XDocument.Load(Locate("tools", "RevoHelper.BootWater", "RevoHelper.BootWater.csproj"));
        var properties = project.Descendants("PropertyGroup")
            .SelectMany(group => group.Elements())
            .ToDictionary(element => element.Name.LocalName, element => element.Value);

        Assert.Equal("true", properties["PublishSingleFile"]);
        Assert.Equal("false", properties["SelfContained"]);
        Assert.Equal("true", properties["IncludeNativeLibrariesForSelfExtract"]);
    }

    private static string Locate(params string[] relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine([root, .. relativePath]);
        Assert.True(File.Exists(path), $"Expected repository file was not found: {path}");
        return path;
    }
}
