using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class ConflictServiceStatusDetectorTests
{
    [Fact]
    public void Detect_ReportsKnownIntelServicesInStableDisplayOrder()
    {
        var detector = new ConflictServiceStatusDetector(() =>
        [
            new("ipfsvc", "Running", "Auto"),
            new("UnrelatedService", "Running", "Auto"),
            new("XtuService", "Stopped", "Auto"),
            new("dptftcs", "Running", "Auto")
        ]);

        var statuses = detector.Detect();

        Assert.Collection(
            statuses,
            status => Assert.Equal(
                new ConflictServiceStatus("XtuService", "Intel XTU Service", false, true, "已停止"),
                status),
            status => Assert.Equal(
                new ConflictServiceStatus("dptftcs", "Intel Dynamic Tuning", true, true, "正在运行"),
                status),
            status => Assert.Equal(
                new ConflictServiceStatus("ipfsvc", "Intel Innovation Platform Framework", true, true, "正在运行"),
                status));
    }

    [Fact]
    public void Detect_ReportsMissingAndUnavailableServicesWithoutThrowing()
    {
        var missing = new ConflictServiceStatusDetector(() => []).Detect();
        var unavailable = new ConflictServiceStatusDetector(
            () => throw new InvalidOperationException("WMI unavailable")).Detect();

        Assert.All(missing, status =>
        {
            Assert.False(status.IsInstalled);
            Assert.False(status.IsRunning);
            Assert.Equal("未安装", status.StatusText);
            Assert.Equal("Missing", status.NativeState);
        });
        Assert.All(unavailable, status =>
        {
            Assert.False(status.IsInstalled);
            Assert.False(status.IsRunning);
            Assert.Equal("状态不可用", status.StatusText);
            Assert.Equal("Unknown", status.NativeState);
        });
    }

    [Theory]
    [InlineData("Start Pending", "正在启动")]
    [InlineData("Stop Pending", "正在停止")]
    [InlineData("Continue Pending", "正在继续")]
    [InlineData("Pause Pending", "正在暂停")]
    [InlineData("Paused", "已暂停")]
    [InlineData("Unknown", "状态不可用")]
    [InlineData("", "状态不可用")]
    public void Detect_DoesNotCallEveryNonRunningServiceStopped(string nativeState, string expectedText)
    {
        var detector = new ConflictServiceStatusDetector(() =>
            [new("XtuService", nativeState, "Auto")]);

        var status = detector.Detect().Single(item => item.ServiceName == "XtuService");

        Assert.True(status.IsInstalled);
        Assert.False(status.IsRunning);
        Assert.Equal(expectedText, status.StatusText);
        Assert.NotEqual("已停止", status.StatusText);
        Assert.Equal(string.IsNullOrEmpty(nativeState) ? "Unknown" : nativeState, status.NativeState);
        Assert.Equal(nativeState.EndsWith(" Pending", StringComparison.OrdinalIgnoreCase), status.IsTransitioning);
    }
}
