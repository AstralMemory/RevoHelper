using RevoHelper.Core.Fans;
using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.OemStatus;

public abstract record OemStatusUpdate;

public sealed record OemCpuStatusUpdate(OemCpuStatus Status) : OemStatusUpdate;

public sealed record OemFanTableUpdate(FanCurveTable Table) : OemStatusUpdate;

public sealed record OemPlatformModeUpdate(PlatformModeSelection Selection) : OemStatusUpdate;

public sealed record OemControlSessionUpdate : OemStatusUpdate;

public sealed record OemConnectionStateUpdate(string State, string Message) : OemStatusUpdate;

public sealed record OemLightingZoneUpdate(LightingZone Zone, LightingZoneState State) : OemStatusUpdate;

public static class OemStatusMessageRouter
{
    public static OemStatusUpdate? Route(string topic, string payload) => topic switch
    {
        "System/CpuInfo" when OemCpuStatusParser.Parse(payload) is { } status =>
            new OemCpuStatusUpdate(status),
        "Fan/Table" when OemFanTableParser.Parse(payload) is { } table =>
            new OemFanTableUpdate(table),
        "Fan/Status" when GcuCpuPolicyStatusParser.Parse(payload) is { } status &&
                          GcuPlatformModeStatusMapper.Map(status) is { } selection =>
            new OemPlatformModeUpdate(selection),
        "Keyboard/Status" when GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Keyboard, payload) is { } state =>
            new OemLightingZoneUpdate(LightingZone.Keyboard, state),
        "HidLightbar/Status" when GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.CoastLightbar, payload) is { } state =>
            new OemLightingZoneUpdate(LightingZone.CoastLightbar, state),
        "HidLightbar_Logo/Status" when GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.Logo, payload) is { } state =>
            new OemLightingZoneUpdate(LightingZone.Logo, state),
        "BT_LC/Status" when GcuQuickControlsStatusParser.ParseLightingZone(LightingZone.LiquidCooler, payload) is { } state =>
            new OemLightingZoneUpdate(LightingZone.LiquidCooler, state),
        _ => null
    };
}
