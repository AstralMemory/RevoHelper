namespace RevoHelper.App.Tests.Packaging;

using RevoHelper.App.Settings;

public sealed class BootWaterPackagingTests
{
    [Fact]
    public void BootHelper_IsShortLivedAndOnlyConnectsRememberedLiquidCoolingDevice()
    {
        var source = File.ReadAllText(Locate("src", "RevoHelper.App", "Settings", "BootWaterHost.cs"));

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

    [Theory]
    [InlineData("same", true)]
    [InlineData("modified", false)]
    [InlineData("truncated", false)]
    [InlineData("appended", false)]
    public void InstallerAcceptsOnlyAnExactCopy(string variant, bool expected)
    {
        var bytes = Enumerable.Range(0, 20000).Select(i => (byte)(i * 31)).ToArray();
        var copy = bytes.ToArray();
        switch (variant)
        {
            case "modified": copy[^1] ^= 1; break;
            case "truncated": copy = copy[..^1]; break;
            case "appended": copy = [.. copy, 0]; break;
        }
        using var source = new MemoryStream(bytes, writable: false);
        using var candidate = new MemoryStream(copy, writable: false);
        Assert.Equal(expected, WindowsBootWaterTaskPlatform.SameContent(source, candidate));
    }

    // Antivirus heuristics flag a program that carries another executable and writes it to disk.
    // The boot task therefore runs a copy of RevoHelper itself.
    [Fact]
    public void MainExecutable_CarriesNoSecondExecutable_AndIsItsOwnBootHelper()
    {
        var project = File.ReadAllText(Locate("src", "RevoHelper.App", "RevoHelper.App.csproj"));
        Assert.DoesNotContain(".exe", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<StartupObject>RevoHelper.App.Program</StartupObject>", project, StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(WindowsBootWaterTaskPlatform).Assembly.GetManifestResourceNames(),
            name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

        var entry = File.ReadAllText(Locate("src", "RevoHelper.App", "Program.cs"));
        Assert.True(entry.IndexOf("BootWaterHost.IsBoot", StringComparison.Ordinal) < entry.IndexOf("App.Main()", StringComparison.Ordinal));
        Assert.True(BootWaterHost.IsBoot(["--boot"]));
        Assert.False(BootWaterHost.IsBoot([]));
        Assert.False(BootWaterHost.IsBoot(["--boot", "x"]));
        // The task that earlier versions registered keeps working: same path, same argument.
        Assert.Contains($"<Arguments>{BootWaterHost.Argument}</Arguments>", BootWaterTaskDefinition.Create(WindowsBootWaterTaskPlatform.InstalledPath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RevoHelper", true)]
    [InlineData("RevoHelper.BootWater", true)]      // installed by versions before 1.0.6
    [InlineData("Something else", false)]
    [InlineData(null, false)]
    public void AnInstalledCopyIsRecognisedByItsProductName(string? product, bool expected) =>
        Assert.Equal(expected, WindowsBootWaterTaskPlatform.IsRevoHelperProduct(product));

    [Fact]
    public void WaterCoolingDrawer_PresentsPreLoginInstallAsSupportedBetaFeature()
    {
        var xaml = File.ReadAllText(Locate("src", "RevoHelper.App", "MainWindow.xaml"));
        Assert.Contains("登录前自动连接", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("登录前自动连接 · 实验", xaml, StringComparison.Ordinal);
    }

    private static string Locate(params string[] relativePath)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var path = Path.Combine([root, .. relativePath]);
        Assert.True(File.Exists(path), $"Expected repository file was not found: {path}");
        return path;
    }
}
