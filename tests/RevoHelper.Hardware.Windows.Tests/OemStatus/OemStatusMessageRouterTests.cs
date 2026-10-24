using RevoHelper.Hardware.Windows.OemStatus;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class OemStatusMessageRouterTests
{
    [Fact]
    public void Route_FanStatus_ProducesTypedPlatformModeUpdate()
    {
        const string payload = """
            {"CPU_PL1":45,"CPU_PL2":65,"CPU_PL1Minimum":5,"CPU_PL1Maximum":150,
             "CPU_PL2Minimum":5,"CPU_PL2Maximum":160,"OperatingMode":2,
             "TurboProfileIndex":0,"Extreme":1,"Silent":0}
            """;

        var update = OemStatusMessageRouter.Route("Fan/Status", payload);

        var platform = Assert.IsType<OemPlatformModeUpdate>(update);
        Assert.Equal(new PlatformModeSelection(PlatformMode.TurboHigh), platform.Selection);
    }

    [Fact]
    public void Route_CpuTopic_ReturnsCpuUpdate()
    {
        var update = OemStatusMessageRouter.Route(
            "System/CpuInfo",
            "{\"CpuTemperature\":\"57\",\"CpuUsage\":\"21\"}");

        var cpu = Assert.IsType<OemCpuStatusUpdate>(update);
        Assert.Equal(57, cpu.Status.TemperatureC);
    }

    [Fact]
    public void Route_FanTableTopic_ReturnsFanUpdate()
    {
        var update = OemStatusMessageRouter.Route(
            "Fan/Table",
            "{\"Name\":\"Balanced\",\"CPU\":[{\"ID\":0,\"UpT\":30,\"DownT\":25,\"Duty\":10}]}");

        var fan = Assert.IsType<OemFanTableUpdate>(update);
        Assert.Equal("Balanced", fan.Table.Name);
    }

    [Theory]
    [InlineData("Keyboard/Status", LightingZone.Keyboard)]
    [InlineData("HidLightbar/Status", LightingZone.CoastLightbar)]
    [InlineData("HidLightbar_Logo/Status", LightingZone.Logo)]
    [InlineData("BT_LC/Status", LightingZone.LiquidCooler)]
    public void Route_LightingStatus_ProducesTypedZoneUpdate(string topic, LightingZone expectedZone)
    {
        var payload = expectedZone == LightingZone.LiquidCooler
            ? "{\"connected\":true,\"LCLED_Mode\":1,\"LCLED_R\":0,\"LCLED_G\":176,\"LCLED_B\":255}"
            : "{\"powerStatus\":\"On\",\"type\":\"MEZone_3p1nd_101\",\"effect\":\"2\"}";

        var update = OemStatusMessageRouter.Route(topic, payload);

        var lighting = Assert.IsType<OemLightingZoneUpdate>(update);
        Assert.Equal(expectedZone, lighting.Zone);
        Assert.True(lighting.State.Available);
    }

    [Theory]
    [InlineData("Fan/Control", "{}")]
    [InlineData("System/CpuInfo", "not-json")]
    [InlineData("Unknown/Topic", "{}")]
    public void Route_UnsupportedOrMalformed_ReturnsNull(string topic, string payload)
    {
        Assert.Null(OemStatusMessageRouter.Route(topic, payload));
    }
}
