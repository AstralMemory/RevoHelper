using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class OemCpuStatusTests
{
    [Theory]
    [InlineData("{\"CpuTemperature\":\"53\",\"CpuUsage\":\"17\",\"CpuFrequency\":\"2795\"}", 53.0, 17.0, 2795.0)]
    [InlineData("{\"CpuTemperature\":61.5}", 61.5, null, null)]
    public void Parse_ReadsStringAndNumericFields(string json, double temperature, double? usage, double? frequency)
    {
        var status = OemCpuStatusParser.Parse(json);

        Assert.NotNull(status);
        Assert.Equal(temperature, status.TemperatureC);
        Assert.Equal(usage, status.UsagePercent);
        Assert.Equal(frequency, status.FrequencyMhz);
    }

    [Theory]
    [InlineData("{\"CpuTemperature\":\"0\"}")]
    [InlineData("{\"CpuUsage\":\"12\"}")]
    [InlineData("not-json")]
    public void Parse_ZeroMissingOrMalformedTemperature_IsUnavailable(string json)
    {
        var status = OemCpuStatusParser.Parse(json);

        Assert.True(status is null || status.TemperatureC is null);
    }
}
