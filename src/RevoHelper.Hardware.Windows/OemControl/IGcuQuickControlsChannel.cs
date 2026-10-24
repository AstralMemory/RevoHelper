using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuQuickControlsChannel
{
    ValueTask<string> ReadSettingStatusAsync(CancellationToken cancellationToken);
    ValueTask<string> ReadDeviceSwitchStatusAsync(CancellationToken cancellationToken);
    ValueTask<string> ReadKeyboardStatusAsync(CancellationToken cancellationToken);
    ValueTask<string> ReadLightingStatusAsync(LightingZone zone, CancellationToken cancellationToken);
    ValueTask<string> ReadBatteryProtectionAsync(CancellationToken cancellationToken);
    ValueTask<string> ReadLiquidCoolingAsync(CancellationToken cancellationToken);
    ValueTask<int?> ReadBatteryChargeLimitAsync(CancellationToken cancellationToken);
    ValueTask WriteSettingAsync(string payload, CancellationToken cancellationToken);
    ValueTask WriteKeyboardAsync(string payload, CancellationToken cancellationToken);
    ValueTask WriteLightingAsync(LightingZone zone, string payload, CancellationToken cancellationToken);
    ValueTask<string> WriteLightingAndReadStatusAsync(
        LightingZone zone,
        string payload,
        CancellationToken cancellationToken);
    ValueTask WriteBatteryProtectionAsync(string payload, CancellationToken cancellationToken);
    ValueTask WriteLiquidCoolingAsync(string payload, CancellationToken cancellationToken);
    ValueTask WriteBatteryChargeLimitAsync(string payload, CancellationToken cancellationToken);
}
