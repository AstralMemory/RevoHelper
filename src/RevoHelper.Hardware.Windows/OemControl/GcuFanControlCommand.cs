using System.Text.Json;
using RevoHelper.Core.Fans;

namespace RevoHelper.Hardware.Windows.OemControl;

public enum FanChannel
{
    Cpu,
    Gpu
}

public static class GcuFanControlCommand
{
    public static string GetTable { get; } = JsonSerializer.Serialize(new
    {
        Action = "GET_FAN_SPEED_CURVE_SETTING"
    });

    public static string SetCurve(FanCurveTable table, FanChannel channel)
    {
        ArgumentNullException.ThrowIfNull(table);
        var points = channel is FanChannel.Cpu ? table.Cpu : table.Gpu;
        if (points.Count != 16)
        {
            throw new ArgumentException("风扇曲线必须包含 16 个点位。", nameof(table));
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Action"] = "SET_FAN_SPEED_CURVE_SETTING",
            ["Name"] = table.Name,
            ["Type"] = channel is FanChannel.Cpu ? "CPU" : "GPU"
        };
        for (var index = 0; index < points.Count; index++)
        {
            payload[$"T{index}"] = points[index].DutyPercent.ToString();
        }
        return JsonSerializer.Serialize(payload);
    }

    public static string Restore(string profileName) => JsonSerializer.Serialize(new
    {
        Action = "RESTORE_FAN_SPEED_CURVE_SETTING",
        Name = profileName
    });

    public static string SetIndependentOutput(string profileName, bool enabled) => JsonSerializer.Serialize(new
    {
        Action = "SET_FAN_CONTROL_RESPECTIVE",
        Name = profileName,
        FanControlRespective = enabled
    });

    public static string SetTransitionSensitivityEnabled(bool enabled) => JsonSerializer.Serialize(new
    {
        Action = "SET_OPERATING_MODE_DETAIL",
        FanSwitchSpeedEnabled = enabled ? "1" : "0"
    });

    public static string SetTransitionSensitivity(int value) => JsonSerializer.Serialize(new
    {
        Action = "SET_OPERATING_MODE_DETAIL",
        FanSwitchSpeed = value.ToString()
    });
}
