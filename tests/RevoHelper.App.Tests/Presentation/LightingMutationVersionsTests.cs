using RevoHelper.App.Presentation;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Presentation;

public sealed class LightingMutationVersionsTests
{
    [Fact]
    public void SeparateZones_CannotCancelEachOthersQueuedChoices()
    {
        var versions = new LightingMutationVersions();
        var keyboardPreset = versions.Reserve(LightingZone.Keyboard);
        var coastColor = versions.Reserve(LightingZone.CoastLightbar);
        versions.Reserve(LightingZone.Logo);
        versions.Reserve(LightingZone.LiquidCooler);
        Assert.True(versions.IsCurrent(LightingZone.Keyboard, keyboardPreset));
        Assert.True(versions.IsCurrent(LightingZone.CoastLightbar, coastColor));
    }

    [Fact]
    public void SameZone_LatestChoiceReplacesOlderPendingChoice()
    {
        var versions = new LightingMutationVersions();
        var old = versions.Reserve(LightingZone.Keyboard);
        var latest = versions.Reserve(LightingZone.Keyboard);
        Assert.False(versions.IsCurrent(LightingZone.Keyboard, old));
        Assert.True(versions.IsCurrent(LightingZone.Keyboard, latest));
        Assert.False(versions.IsCurrent(LightingZone.Logo, latest));
    }
}
