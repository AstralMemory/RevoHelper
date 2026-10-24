using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class RyzenAdjProcessClientTests
{
    [Fact]
    public void GetDefaultExecutablePath_ForMainApplication_UsesBundledToolsFolder()
    {
        var applicationDirectory = Path.Combine("C:\\RevoHelper", "package");

        var result = RyzenAdjProcessClient.GetDefaultExecutablePath(applicationDirectory);

        Assert.Equal(
            Path.Combine(applicationDirectory, "tools", "ryzenadj", "ryzenadj.exe"),
            result);
    }

    [Fact]
    public void GetDefaultExecutablePath_ForPackagedProbe_UsesSiblingRyzenAdjFolder()
    {
        var packageDirectory = Path.Combine("C:\\RevoHelper", "package");
        var probeDirectory = Path.Combine(packageDirectory, "tools", "probe");

        var result = RyzenAdjProcessClient.GetDefaultExecutablePath(probeDirectory);

        Assert.Equal(
            Path.Combine(packageDirectory, "tools", "ryzenadj", "ryzenadj.exe"),
            result);
    }
}
