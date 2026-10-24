using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuPlatformModeCommand
{
    public static string SelectMode(PlatformModeSelection selection)
    {
        var action = selection.Mode switch
        {
            PlatformMode.Office => "OPERATING_OFFICE_MODE",
            PlatformMode.Balanced => "OPERATING_GAMING_MODE",
            PlatformMode.TurboQuiet or PlatformMode.TurboHigh => "OPERATING_TURBO_MODE",
            PlatformMode.Custom => "OPERATING_CUSTOM_MODE",
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["Action"] = action,
            ["ProfileIndex"] = selection.Mode == PlatformMode.Custom
                ? selection.ProfileIndex - 1
                : selection.ProfileIndex
        });
    }

    public static string SelectTurboStrategy(PlatformMode mode) => mode switch
    {
        PlatformMode.TurboQuiet => "{\"Action\":\"SET_CPU_CORE_OFFSET_SILENT\",\"SILENT\":1}",
        PlatformMode.TurboHigh => "{\"Action\":\"SET_CPU_CORE_OFFSET_EXTREME\",\"EXTREME\":1}",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static string CompanionMode(PlatformMode mode) => mode switch
    {
        PlatformMode.Office => "{\"IsNormalRun\":0}",
        PlatformMode.Balanced => "{\"IsNormalRun\":1}",
        PlatformMode.TurboQuiet or PlatformMode.TurboHigh => "{\"IsNormalRun\":2}",
        PlatformMode.Custom => "{\"IsCustomRun\":true}",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
