using RevoHelper.App.Lifecycle;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class ControlConflictDetectorTests
{
    [Fact]
    public void Detect_RecognizesCurrentCcuWinUiProcessName()
    {
        var detector = new ControlConflictDetector(() => [new ProcessIdentity(42, "CCUWinUI")]);

        var conflict = Assert.Single(detector.Detect());

        Assert.Contains("机械革命官方控制台", conflict.DisplayName, StringComparison.Ordinal);
    }
    [Fact]
    public void Detect_FindsRunningOemControlCenterFrontends()
    {
        var detector = new ControlConflictDetector(() =>
        [
            new(10, "ControlCenterU"),
            new(11, "GamingCenterU")
        ]);

        var conflicts = detector.Detect();

        Assert.Single(conflicts);
        Assert.Equal("MECHREVO Control Center（机械革命官方控制台）", conflicts[0].DisplayName);
    }

    [Fact]
    public void Detect_PlacesOfficialControlCenterFirst()
    {
        var detector = new ControlConflictDetector(() =>
        [
            new(10, "ThrottleStop"),
            new(11, "ControlCenterU")
        ]);

        var conflicts = detector.Detect();

        Assert.Equal(
            ["MECHREVO Control Center（机械革命官方控制台）", "ThrottleStop"],
            conflicts.Select(conflict => conflict.DisplayName));
    }

    [Fact]
    public void Detect_FindsKnownThirdPartyTuningApplications()
    {
        var detector = new ControlConflictDetector(() =>
        [
            new(10, "ThrottleStop"),
            new(11, "L-Mechrevo"),
            new(12, "Universal x86 Tuning Utility"),
            new(13, "XtuUiLauncher")
        ]);

        var conflicts = detector.Detect();

        Assert.Equal(
            ["ThrottleStop", "L-Mechrevo", "Universal x86 Tuning Utility", "Intel XTU"],
            conflicts.Select(conflict => conflict.DisplayName));
    }

    [Fact]
    public void Detect_IgnoresRequiredOrPassiveBackgroundComponents()
    {
        var detector = new ControlConflictDetector(() =>
        [
            new(10, "GCUBridge"),
            new(11, "GCUService"),
            new(12, "OSDTpDetect"),
            new(13, "MRAfterSaleService"),
            new(14, "XtuService"),
            new(15, "RevoHelper")
        ]);

        Assert.Empty(detector.Detect());
    }

    [Fact]
    public void Detect_WhenProcessEnumerationFails_DoesNotBlockStartup()
    {
        var detector = new ControlConflictDetector(
            () => throw new InvalidOperationException("process access denied"));

        Assert.Empty(detector.Detect());
    }
}
