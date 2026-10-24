namespace RevoHelper.Core.Control;

public static class LightingProfileCatalog
{
    public const int CurrentSchemaVersion = 1;

    public static IReadOnlySet<int> OfficialSleepSeconds { get; } = new HashSet<int>
    {
        0, 10, 15, 20, 30, 45, 60, 120, 300, 600, 1200, 1800, 3600, 7200, 10800
    };

    public static LightingProfile OemDefault { get; } = new(
        CurrentSchemaVersion,
        "oem-default",
        "机器最初始默认背光",
        [Zone(LightingZone.Keyboard, LightingEffect.OemDefault, 3, 2, new(128, 0, 255))]);

    public static IReadOnlyList<LightingProfile> BuiltIns { get; } =
    [
        new(CurrentSchemaVersion, "revo-flow", "耀世流光",
        [
            Zone(LightingZone.Keyboard, LightingEffect.Rainbow, 4, 3, new(126, 64, 255))
        ]),
        new(CurrentSchemaVersion, "ice-breath", "冰海呼吸",
        [
            Zone(LightingZone.Keyboard, LightingEffect.Breathing, 3, 2, new(0, 176, 255))
        ]),
        new(CurrentSchemaVersion, "quiet-night", "静夜微光",
        [
            Zone(LightingZone.Keyboard, LightingEffect.Static, 1, 1, new(28, 42, 92))
        ])
    ];

    public static bool Validate(LightingProfile? profile, out string reason)
    {
        if (profile is null)
        {
            reason = "方案内容为空。";
            return false;
        }
        if (profile.SchemaVersion != CurrentSchemaVersion)
        {
            reason = $"不支持灯光方案版本 {profile.SchemaVersion}。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Name))
        {
            reason = "方案名称或标识为空。";
            return false;
        }
        if (profile.Zones is null || profile.Zones.Count == 0)
        {
            reason = "方案没有可应用的灯光分区。";
            return false;
        }
        if (profile.Zones.GroupBy(zone => zone.Zone).Any(group => group.Count() > 1))
        {
            reason = "方案包含重复灯光分区。";
            return false;
        }
        if (profile.Zones.Count != 1 || profile.Zones[0].Zone != LightingZone.Keyboard)
        {
            reason = "Revo 灯光方案仅保存键盘背光；其它灯区请在各自页面单独调整。";
            return false;
        }
        foreach (var zone in profile.Zones)
        {
            if (zone.Brightness is < 0 or > 4)
            {
                reason = "灯光亮度必须在 0–4 之间。";
                return false;
            }
            if (zone.Speed is < 1 or > 4)
            {
                reason = "灯效速度必须在 1–4 之间。";
                return false;
            }
            if (!OfficialSleepSeconds.Contains(zone.SleepSeconds))
            {
                reason = "灯光自动熄灯时间必须使用官方支持的秒级档位。";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }

    private static LightingZoneConfiguration Zone(
        LightingZone zone,
        LightingEffect effect,
        int brightness,
        int speed,
        LightingRgb color) =>
        new(zone, true, effect, color, brightness, speed, LightingDirection.Left, 120);
}
