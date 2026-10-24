using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class GcuControlSessionConflictDetectorTests
{
    [Fact]
    public void GcuMqttSource_DoesNotYieldOrPollOfficialProcesses()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.Hardware.Windows", "OemStatus", "GcuMqttStatusSource.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("GcuFrontendSessionArbiter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("官方控制台正在使用控制通道", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitUntilAvailableAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GcuMqttSource_UsesTheBrokerAuthenticatedOemClientIdentity()
    {
        var sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "RevoHelper.Hardware.Windows", "OemStatus", "GcuMqttStatusSource.cs"));
        var source = File.ReadAllText(sourcePath);

        Assert.Contains("ClientId = \"UWPClient_6\"", source, StringComparison.Ordinal);
        Assert.Contains("UserName = \"UWPClient_User_6\"", source, StringComparison.Ordinal);
        Assert.Contains(".WithClientId(sessionClientId)", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".WithClientId(CreateClientId(Environment.ProcessId))", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CCUWinUI")]
    [InlineData("ccuwinui")]
    [InlineData("ControlCenterU")]
    [InlineData("GamingCenterU")]
    [InlineData("L-Mechrevo-beta5.1")]
    [InlineData("L-Mechrevo-beta6")]
    public void IsKnownConflictingProcessName_RecognizesGcuControllers(string processName) =>
        Assert.True(GcuControlSessionConflictDetector.IsKnownConflictingProcessName(processName));

    [Theory]
    [InlineData("RevoHelper")]
    [InlineData("CoreTemp")]
    [InlineData("explorer")]
    public void IsKnownConflictingProcessName_DoesNotFlagUnrelatedProcesses(string processName) =>
        Assert.False(GcuControlSessionConflictDetector.IsKnownConflictingProcessName(processName));

}
