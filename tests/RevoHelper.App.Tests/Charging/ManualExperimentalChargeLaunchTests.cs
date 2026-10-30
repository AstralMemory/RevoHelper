using RevoHelper.App.Charging;

namespace RevoHelper.App.Tests.Charging;

public sealed class ManualExperimentalChargeLaunchTests
{
    [Theory]
    [InlineData("60", 60)] [InlineData("80", 80)] [InlineData("100", 100)]
    public void ExactExplicitRiskAcceptingLaunchIsRecognized(string text, int expected) =>
        Assert.Equal(expected, ManualExperimentalChargeLaunch.ReadLimit(["--manual-charge-limit-accept-platform-risk", text]));

    [Fact]
    public void NormalStartupSettingsOrIncompleteArgsNeverEnable()
    {
        foreach (var args in new string[][] { [], ["--startup"], ["--manual-charge-limit-accept-platform-risk"],
            ["--manual-charge-limit-accept-platform-risk", "60", "--startup"],
            ["--manual-charge-limit-accept-platform-risk", "70"], ["--manual-charge-limit", "60"] })
            Assert.Null(ManualExperimentalChargeLaunch.ReadLimit(args));
    }
}
