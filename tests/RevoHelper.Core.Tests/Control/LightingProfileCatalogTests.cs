using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class LightingProfileCatalogTests
{
    [Fact]
    public void BuiltIns_ExposeThreeNamedVersionedSchemes()
    {
        Assert.Equal(
            ["耀世流光", "冰海呼吸", "静夜微光"],
            LightingProfileCatalog.BuiltIns.Select(profile => profile.Name));
        Assert.All(LightingProfileCatalog.BuiltIns, profile => Assert.Equal(1, profile.SchemaVersion));
    }

    [Fact]
    public void BuiltIns_AreKeyboardOnlyAndCannotWriteOtherPhysicalZones()
    {
        Assert.All(LightingProfileCatalog.BuiltIns, profile =>
        {
            var zone = Assert.Single(profile.Zones);
            Assert.Equal(LightingZone.Keyboard, zone.Zone);
        });
    }

    [Fact]
    public void OemDefault_UsesOnlyTheProvenKeyboardRoute()
    {
        var profile = LightingProfileCatalog.OemDefault;

        var zone = Assert.Single(profile.Zones);
        Assert.Equal(LightingZone.Keyboard, zone.Zone);
        Assert.Equal(LightingEffect.OemDefault, zone.Effect);
        Assert.True(zone.Enabled);
    }

    [Fact]
    public void Validate_RejectsBrightnessOutsideHardwareRange()
    {
        var invalid = new LightingProfile(1, "bad", "bad",
        [
            new(LightingZone.Keyboard, true, LightingEffect.Static, new(0, 255, 255), 5, 2, LightingDirection.Left, 2)
        ]);

        Assert.False(LightingProfileCatalog.Validate(invalid, out var reason));
        Assert.Contains("亮度", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDuplicateZones()
    {
        var zone = new LightingZoneConfiguration(
            LightingZone.Keyboard, true, LightingEffect.Static, new(0, 255, 255), 3, 2, LightingDirection.Left, 2);
        var invalid = new LightingProfile(1, "duplicate", "duplicate", [zone, zone]);

        Assert.False(LightingProfileCatalog.Validate(invalid, out var reason));
        Assert.Contains("重复", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsShareableProfilesThatContainNonKeyboardZones()
    {
        var invalid = new LightingProfile(1, "cross-zone", "cross-zone",
        [
            new(LightingZone.Keyboard, true, LightingEffect.Static, new(0, 255, 255), 3, 2, LightingDirection.Left, 0),
            new(LightingZone.LiquidCooler, true, LightingEffect.Rainbow, new(0, 255, 255), 3, 2, LightingDirection.Left, 0)
        ]);

        Assert.False(LightingProfileCatalog.Validate(invalid, out var reason));
        Assert.Contains("仅保存键盘背光", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ZoneConfiguration_UsesSecondsForOfficialLightingSleepValue()
    {
        var zone = new LightingZoneConfiguration(
            LightingZone.Keyboard, true, LightingEffect.Static, new(0, 255, 255), 3, 2,
            LightingDirection.None, 120);

        Assert.Equal(120, zone.SleepSeconds);
        Assert.Null(typeof(LightingZoneConfiguration).GetProperty("SleepMinutes"));
    }

    [Fact]
    public void Validate_RejectsUnsupportedLightingSleepSeconds()
    {
        var profile = new LightingProfile(1, "bad-sleep", "bad-sleep",
        [
            new(LightingZone.Keyboard, true, LightingEffect.Static, new(0, 255, 255), 3, 2,
                LightingDirection.None, 2)
        ]);

        Assert.False(LightingProfileCatalog.Validate(profile, out var reason));
        Assert.Contains("秒", reason, StringComparison.Ordinal);
    }
}
