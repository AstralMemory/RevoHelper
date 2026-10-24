using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class ProfileCompositionTests
{
    [Fact]
    public void Profile_KeepsAllSixPoliciesIndependent()
    {
        var profile = new ProfileComposition(
            "Daily",
            PlatformMode.Balanced,
            new CpuPolicy(45, 80, 4000),
            new GpuPolicy(GpuOperatingMode.Hybrid, 120),
            new FanCurvePolicy("daily-quiet"),
            new LightingPolicy(LightingBehavior.PlatformModeColor),
            new LiquidCoolingPolicy(LiquidCoolingStrategy.Quieter));

        Assert.Equal(PlatformMode.Balanced, profile.PlatformMode);
        Assert.Equal(45, profile.Cpu?.Pl1Watts);
        Assert.Equal(GpuOperatingMode.Hybrid, profile.Gpu?.OperatingMode);
        Assert.Equal("daily-quiet", profile.Fan?.CurveId);
        Assert.Equal(LightingBehavior.PlatformModeColor, profile.Lighting?.Behavior);
        Assert.Equal(LiquidCoolingStrategy.Quieter, profile.LiquidCooling?.Strategy);
    }
}
