using System.Text.Json;
using RevoHelper.Core.Fans;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuFanControlCommandTests
{
    [Fact]
    public void SetCurve_UsesTheOfficialNameTypeAndSixteenDutyFields()
    {
        var table = CreateTable("Profile2");

        using var json = JsonDocument.Parse(GcuFanControlCommand.SetCurve(table, FanChannel.Cpu));
        var root = json.RootElement;

        Assert.Equal("SET_FAN_SPEED_CURVE_SETTING", root.GetProperty("Action").GetString());
        Assert.Equal("Profile2", root.GetProperty("Name").GetString());
        Assert.Equal("CPU", root.GetProperty("Type").GetString());
        for (var index = 0; index < 16; index++)
        {
            var expected = index == 15 ? 100 : 20 + index * 5;
            Assert.Equal(expected.ToString(), root.GetProperty($"T{index}").GetString());
        }
    }

    [Fact]
    public void RestoreAndResponseCommands_MatchTheInstalledOfficialCcuContract()
    {
        AssertJson(GcuFanControlCommand.Restore("Profile3"), "RESTORE_FAN_SPEED_CURVE_SETTING", "Name", "Profile3");
        AssertJson(GcuFanControlCommand.SetIndependentOutput("Profile3", false), "SET_FAN_CONTROL_RESPECTIVE", "FanControlRespective", false);
        AssertJson(GcuFanControlCommand.SetTransitionSensitivityEnabled(true), "SET_OPERATING_MODE_DETAIL", "FanSwitchSpeedEnabled", "1");
        AssertJson(GcuFanControlCommand.SetTransitionSensitivity(18), "SET_OPERATING_MODE_DETAIL", "FanSwitchSpeed", "18");
    }

    private static void AssertJson<T>(string payload, string action, string property, T expected)
    {
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        Assert.Equal(action, root.GetProperty("Action").GetString());
        Assert.Equal(expected, JsonSerializer.Deserialize<T>(root.GetProperty(property).GetRawText()));
    }

    private static FanCurveTable CreateTable(string name)
    {
        static FanCurvePoint Point(int index) => new(index, 40 + index * 3, 38 + index * 3, 20 + index * 5);
        var points = Enumerable.Range(0, 16).Select(Point).ToArray();
        points[^1] = points[^1] with { DutyPercent = 100 };
        return new(name, true, points, points);
    }
}
