using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class LightingStateAccumulatorTests
{
    [Theory]
    [InlineData(LightingZone.Keyboard)]
    [InlineData(LightingZone.CoastLightbar)]
    [InlineData(LightingZone.Logo)]
    [InlineData(LightingZone.LiquidCooler)]
    public void Merge_MissingColorPreservesSelectionButExplicitBlackIsAnObservation(LightingZone zone)
    {
        var config = new LightingZoneConfiguration(zone, true, LightingEffect.Static, new(36, 214, 106),
            4, 3, LightingDirection.None, 120);
        var previous = new LightingZoneState(true, "device", config, [LightingEffect.Static]);
        var partial = new LightingZoneState(true, null, config with { Color = new(0, 0, 0) }, [],
            false, LightingReportedFields.Power);

        Assert.Equal(config.Color, LightingStateAccumulator.Merge(previous, partial)!.Configuration!.Color);
        var explicitBlack = partial with { ReportedFields = LightingReportedFields.Power | LightingReportedFields.Color };
        Assert.Equal(new LightingRgb(0, 0, 0), LightingStateAccumulator.Merge(previous, explicitBlack)!.Configuration!.Color);
        Assert.Equal(config.Color, LightingStateAccumulator.Merge(previous,
            explicitBlack with { Configuration = explicitBlack.Configuration! with { Enabled = false } })!.Configuration!.Color);
    }
}
