using RevoHelper.Core.Dashboard;

namespace RevoHelper.Core.Tests.Dashboard;

public sealed class PerformanceTradeoffCatalogTests
{
    [Theory]
    [InlineData("office", TurboStrategy.Quiet, 50, 30, 15)]
    [InlineData("balanced", TurboStrategy.Quiet, 80, 65, 50)]
    [InlineData("turbo", TurboStrategy.Quiet, 92, 80, 60)]
    [InlineData("turbo", TurboStrategy.HighEfficiency, 100, 70, 80)]
    public void Get_ReturnsTheReferenceValuesFromTheHomePageDesign(
        string profileId,
        TurboStrategy turboStrategy,
        int performance,
        int temperature,
        int noise)
    {
        var tradeoff = PerformanceTradeoffCatalog.Get(profileId, turboStrategy);

        Assert.Equal(performance, tradeoff.Performance);
        Assert.Equal(temperature, tradeoff.Temperature);
        Assert.Equal(noise, tradeoff.Noise);
    }
}
