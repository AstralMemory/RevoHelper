namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class LightingProbeContractTests
{
    [Fact]
    public void LightingWrites_UseZoneSpecificReadbackPolicy()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "RevoHelper.Hardware.Windows", "OemStatus", "GcuMqttStatusSource.cs")));
        Assert.Contains("LightingStatusReadback.WaitAsync(zone, waiter.Task", source, StringComparison.Ordinal);
        Assert.DoesNotContain("waiter.Task.WaitAsync(TimeSpan.FromMilliseconds(650)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ExposesReadOnlyRawStatusForEveryLightingZone()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tools", "RevoHelper.Probe", "Program.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("args[0].Equals(\"lighting\"", source, StringComparison.Ordinal);
        Assert.Contains("args[1].Equals(\"status\"", source, StringComparison.Ordinal);
        Assert.Contains("Enum.GetValues<LightingZone>()", source, StringComparison.Ordinal);
        Assert.Contains("ReadLightingStatusAsync(zone", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteLightingAsync(zone", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Probe_ExposesReadOnlyGpuOutputStatus()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "tools", "RevoHelper.Probe", "Program.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("args[0].Equals(\"gpu-output\"", source, StringComparison.Ordinal);
        Assert.Contains("IGcuGpuOutputModeChannel", source, StringComparison.Ordinal);
        Assert.Contains("ReadStatusAsync(timeout.Token)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("gpuChannel.WriteModeAsync", source, StringComparison.Ordinal);
    }
}
