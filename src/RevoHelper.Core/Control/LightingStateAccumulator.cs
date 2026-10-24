namespace RevoHelper.Core.Control;

/// <summary>Combines only fields actually present in a hardware packet; defaults are not observations.</summary>
public static class LightingStateAccumulator
{
    public static LightingZoneState? Merge(LightingZoneState? previous, LightingZoneState? update)
    {
        if (update is null) return previous;
        var metadata = update with
        {
            Available = update.HasAvailability ? update.Available : previous?.Available ?? false,
            HardwareType = update.HardwareType ?? previous?.HardwareType,
            SupportedEffects = update.SupportedEffects.Count > 0
                ? update.SupportedEffects : previous?.SupportedEffects ?? []
        };
        if (update.Configuration is not { } incoming)
            return metadata with { Configuration = previous?.Configuration };

        var basis = previous?.Configuration ?? new LightingZoneConfiguration(
            incoming.Zone, incoming.Enabled, LightingEffect.Static, new(0, 176, 255),
            3, 2, LightingDirection.None, 120);
        var fields = update.EffectiveReportedFields;
        var enabled = fields.HasFlag(LightingReportedFields.Power) ? incoming.Enabled : basis.Enabled;
        // Off reports contain black/zero placeholders even when the JSON has every field.
        if (!enabled) return metadata with { Configuration = basis with { Enabled = false } };

        var effect = fields.HasFlag(LightingReportedFields.Effect) ? incoming.Effect : basis.Effect;
        var usesColor = effect is not (LightingEffect.OemDefault or LightingEffect.Rainbow or
            LightingEffect.Colorful or LightingEffect.ColorMarquee);
        return metadata with
        {
            Configuration = basis with
            {
                Enabled = enabled,
                Effect = effect,
                Color = usesColor && fields.HasFlag(LightingReportedFields.Color) ? incoming.Color : basis.Color,
                Brightness = fields.HasFlag(LightingReportedFields.Brightness) ? incoming.Brightness : basis.Brightness,
                Speed = fields.HasFlag(LightingReportedFields.Speed)
                    ? DecodeSpeed(incoming.Zone, update.RawSpeed, metadata.HardwareType, incoming.Speed) : basis.Speed,
                Direction = fields.HasFlag(LightingReportedFields.Direction) ? incoming.Direction : basis.Direction
            }
        };
    }

    private static int DecodeSpeed(LightingZone zone, int? raw, string? type, int fallback) =>
        zone == LightingZone.Keyboard && raw is not null &&
        type?.StartsWith("MEZone_", StringComparison.OrdinalIgnoreCase) == true
            ? raw switch { 7 => 1, 5 => 2, 3 => 3, 1 => 4, _ => 1 }
            : fallback;
}
