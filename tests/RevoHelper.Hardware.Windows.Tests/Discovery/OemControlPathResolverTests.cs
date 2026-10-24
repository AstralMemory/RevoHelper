using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class OemControlPathResolverTests
{
    [Theory]
    [InlineData(@"C:\Program Files\OEM\Control\AiStoneService\GCUBridge.exe", @"C:\Program Files\OEM\Control\AiStoneService\MyControlCenter")]
    [InlineData("\"D:\\OEM Tools\\AiStoneService\\GCUBridge.exe\" --service", @"D:\OEM Tools\AiStoneService\MyControlCenter")]
    public void ResolveFromServiceImagePath_DerivesSiblingControlDirectory(string imagePath, string expected)
    {
        Assert.Equal(expected, OemControlPathResolver.ResolveFromServiceImagePath(imagePath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\SystemRoot\System32\drivers\UWACPIDriver.sys")]
    public void ResolveFromServiceImagePath_RejectsMissingOrNonExecutablePaths(string? imagePath)
    {
        Assert.Null(OemControlPathResolver.ResolveFromServiceImagePath(imagePath));
    }
}
