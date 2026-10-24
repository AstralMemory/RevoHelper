using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class OemFanTableParserTests
{
    [Fact]
    public void Parse_MapsCpuAndGpuPointsWithoutChangingValues()
    {
        var json = Payload(includeGpu: true);

        var table = OemFanTableParser.Parse(json);

        Assert.NotNull(table);
        Assert.Equal("M4T2", table.Name);
        Assert.True(table.IndependentCpuGpuControl);
        Assert.Equal(16, table.Cpu.Count);
        Assert.Equal(16, table.Gpu.Count);
        Assert.Equal((0, 46, 43, 0), Values(table.Cpu[0]));
        Assert.Equal((15, 255, 255, 100), Values(table.Gpu[15]));
    }

    [Fact]
    public void Parse_CpuOnlyPayload_PreservesCpuAndLeavesGpuEmpty()
    {
        var table = OemFanTableParser.Parse(Payload(includeGpu: false));

        Assert.NotNull(table);
        Assert.Equal(16, table.Cpu.Count);
        Assert.Empty(table.Gpu);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public void Parse_MalformedOrEmptyPayload_ReturnsNull(string json) =>
        Assert.Null(OemFanTableParser.Parse(json));

    private static string Payload(bool includeGpu)
    {
        var points = Enumerable.Range(0, 16)
            .Select(index => new
            {
                ID = index,
                UpT = index == 15 ? 255 : 46 + index * 3,
                DownT = index == 15 ? 255 : 43 + index * 3,
                Duty = index == 15 ? 100 : index * 6
            })
            .ToArray();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Activated = true,
            Name = "M4T2",
            FanControlRespective = true,
            CPU = points,
            GPU = includeGpu ? points : null
        });
    }

    private static (int, int, int, int) Values(RevoHelper.Core.Fans.FanCurvePoint point) =>
        (point.Id, point.UpTemperatureC, point.DownTemperatureC, point.DutyPercent);
}
