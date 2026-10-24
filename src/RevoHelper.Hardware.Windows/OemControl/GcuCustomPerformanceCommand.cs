using System.Globalization;
using System.Text.Json;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuCustomPerformanceCommand
{
    private const string Action = "SET_OPERATING_MODE_DETAIL";

    public static string CpuTemperatureWallSwitch(bool enabled) =>
        JsonSerializer.Serialize(new CpuTccSwitchCommand(Action, Bit(enabled)));

    public static string CpuTemperatureWall(int temperatureC) =>
        JsonSerializer.Serialize(new CpuTccCommand(Action, temperatureC));

    public static string CpuBurstPowerLimit(int watts) =>
        JsonSerializer.Serialize(new Pl4Command(Action, Text(watts)));

    public static string CpuAdvancedMenu(bool enabled) => enabled
        ? JsonSerializer.Serialize(new CpuAdvancedOnCommand(Action, "1"))
        : JsonSerializer.Serialize(new CpuAdvancedOffCommand(Action, "0"));

    public static string OverclockingSwitch(bool enabled) =>
        JsonSerializer.Serialize(new OverclockingCommand(Action, Bit(enabled)));

    public static string GpuConfigurableTgpSwitch(bool enabled) =>
        JsonSerializer.Serialize(new GpuTgpSwitchCommand(Action, Bit(enabled)));

    public static string GpuConfigurableTgp(int watts) =>
        JsonSerializer.Serialize(new GpuTgpCommand(Action, Text(watts)));

    public static string GpuDynamicBoostSwitch(bool enabled) =>
        JsonSerializer.Serialize(new GpuDynamicBoostSwitchCommand(Action, Bit(enabled)));

    public static string GpuDynamicBoost(int watts) =>
        JsonSerializer.Serialize(new GpuDynamicBoostCommand(Action, Text(watts)));

    public static string GpuCoreOffset(int mhz) =>
        JsonSerializer.Serialize(new GpuCoreOffsetCommand(Action, Text(mhz)));

    public static string GpuMemoryOffset(int mhz) =>
        JsonSerializer.Serialize(new GpuMemoryOffsetCommand(Action, Text(mhz)));

    private static string Bit(bool enabled) => enabled ? "1" : "0";
    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record CpuTccSwitchCommand(string Action, string CpuTccOffsetSwitch);
    private sealed record CpuTccCommand(string Action, int CpuTccOffset);
    private sealed record Pl4Command(string Action, string PL4);
    private sealed record CpuAdvancedOnCommand(string Action, string CPUPerformanceAndOverClockMenuSwitch_ON);
    private sealed record CpuAdvancedOffCommand(string Action, string CPUPerformanceAndOverClockMenuSwitch_OFF);
    private sealed record OverclockingCommand(string Action, string OverClockingSwitch);
    private sealed record GpuTgpSwitchCommand(string Action, string GpuConfigurableTGPSwitch);
    private sealed record GpuTgpCommand(string Action, string GpuConfigurableTGPTarget);
    private sealed record GpuDynamicBoostSwitchCommand(string Action, string GpuDynamicBoostSwitch);
    private sealed record GpuDynamicBoostCommand(string Action, string GpuDynamicBoost);
    private sealed record GpuCoreOffsetCommand(string Action, string GpuCoreClockOffsetOC);
    private sealed record GpuMemoryOffsetCommand(string Action, string GpuMemoryClockOffsetOC);
}
