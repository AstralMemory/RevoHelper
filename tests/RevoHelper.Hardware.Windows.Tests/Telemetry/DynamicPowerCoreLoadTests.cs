using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class DynamicPowerCoreLoadTests
{
    [Fact]
    public void MaximumCoreLoadUsesOnlyIndividualCoreOrThreadLoadSensors()
    {
        var telemetry = LibreSensorSelector.SelectCpu([
            new("CPU Total", SensorValueKind.Load, 80),
            new("CPU Core Max", SensorValueKind.Load, 100),
            new("CPU Core #1", SensorValueKind.Load, 15),
            new("CPU Core #2 Thread #1", SensorValueKind.Load, 45),
            new("P-Core #3", SensorValueKind.Load, 25),
            new("E-Core #4", SensorValueKind.Load, 30),
            new("CPU Core #5", SensorValueKind.Clock, 5000)]);
        Assert.Equal(45, telemetry.MaximumCoreUsagePercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1d)]
    [InlineData(101d)]
    public void AnyInvalidIndividualCoreMakesMaximumUnavailable(double? invalid)
    {
        var telemetry = LibreSensorSelector.SelectCpu([
            new("CPU Core #1", SensorValueKind.Load, 1),
            new("CPU Core #2", SensorValueKind.Load, invalid)]);
        Assert.Null(telemetry.MaximumCoreUsagePercent);
    }

    [Fact]
    public void AggregateSensorsAreNeverUsedAsFakeSingleCoreTelemetry()
    {
        var telemetry = LibreSensorSelector.SelectCpu([
            new("CPU Total", SensorValueKind.Load, 1),
            new("CPU Core Max", SensorValueKind.Load, 1)]);
        Assert.Null(telemetry.MaximumCoreUsagePercent);
    }
}
