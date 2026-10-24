using RevoHelper.App.Settings;

namespace RevoHelper.App.Tests.Settings;

public sealed class StartupCommandTests
{
    [Fact]
    public void Build_AddsExplicitWindowsStartupMarker()
    {
        Assert.Equal(
            "\"C:\\Apps\\RevoHelper.exe\" --startup",
            StartupCommand.Build("C:\\Apps\\RevoHelper.exe"));
    }

    [Theory]
    [InlineData("--startup")]
    [InlineData("--STARTUP")]
    public void IsStartupLaunch_RecognizesMarkerIgnoringCase(string argument)
    {
        Assert.True(StartupCommand.IsStartupLaunch([argument]));
    }

    [Fact]
    public void MatchesExecutable_AcceptsQuotedCurrentPathIgnoringCase()
    {
        Assert.True(StartupCommand.MatchesExecutable(
            "\"C:\\Apps\\RevoHelper.exe\"",
            "c:\\apps\\REVOHELPER.EXE"));
    }

    [Fact]
    public void MatchesExecutable_AcceptsRegisteredStartupArguments()
    {
        Assert.True(StartupCommand.MatchesExecutable(
            "\"C:\\Apps\\RevoHelper.exe\" --startup",
            "C:\\Apps\\RevoHelper.exe"));
    }

    [Fact]
    public void MatchesExecutable_RejectsStalePortablePath()
    {
        Assert.False(StartupCommand.MatchesExecutable(
            "\"C:\\Old\\RevoHelper.exe\"",
            "C:\\New\\RevoHelper.exe"));
    }

    [Theory]
    [InlineData(0x03, true)]
    [InlineData(0x02, false)]
    [InlineData(0x00, false)]
    public void StartupApproval_RecognizesWindowsDisabledState(byte marker, bool expected)
    {
        Assert.Equal(expected, StartupApproval.IsDisabled(new byte[] { marker, 0, 0, 0 }));
    }
}
