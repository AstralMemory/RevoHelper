namespace RevoHelper.Core.Control;

public enum LightingZone
{
    Keyboard,
    CoastLightbar,
    Logo,
    LiquidCooler
}

public enum LightingEffect
{
    OemDefault,
    Static,
    Breathing,
    Wave,
    Rainbow,
    Reactive,
    Ripple,
    Raindrop,
    ColorMarquee,
    Fireworks,
    Aurora,
    Game,
    Impact,
    Meteor,
    Colorful,
    Rotate
}

public enum LightingDirection
{
    None,
    Left,
    Right,
    Up,
    Down
}

public sealed record LightingRgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed record LightingZoneConfiguration(
    LightingZone Zone,
    bool Enabled,
    LightingEffect Effect,
    LightingRgb Color,
    int Brightness,
    int Speed,
    LightingDirection Direction,
    int SleepSeconds);

[Flags]
public enum LightingReportedFields
{
    None = 0,
    Power = 1,
    Effect = 2,
    Color = 4,
    Brightness = 8,
    Speed = 16,
    Direction = 32,
    All = Power | Effect | Color | Brightness | Speed | Direction
}

public sealed record LightingZoneState(
    bool Available,
    string? HardwareType,
    LightingZoneConfiguration? Configuration,
    IReadOnlyList<LightingEffect> SupportedEffects,
    bool HasDetailedConfiguration = true,
    LightingReportedFields? ReportedFields = null,
    bool HasAvailability = true,
    int? RawSpeed = null)
{
    public LightingReportedFields EffectiveReportedFields => ReportedFields ??
        (HasDetailedConfiguration ? LightingReportedFields.All : LightingReportedFields.Power);
}

public sealed record LightingProfile(
    int SchemaVersion,
    string Id,
    string Name,
    IReadOnlyList<LightingZoneConfiguration> Zones);
