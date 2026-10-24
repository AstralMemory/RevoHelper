using LibreHardwareMonitor.Hardware;

namespace RevoHelper.Hardware.Windows.Telemetry;

public static class LibreSensorDescriptorFactory
{
    public static SensorDescriptor? Create(string name, SensorType sensorType, float? value)
    {
        var kind = sensorType switch
        {
            SensorType.Load => SensorValueKind.Load,
            SensorType.Temperature => SensorValueKind.Temperature,
            SensorType.Power => SensorValueKind.Power,
            SensorType.Clock => SensorValueKind.Clock,
            SensorType.SmallData => SensorValueKind.Memory,
            _ => (SensorValueKind?)null
        };

        return kind is null ? null : new(name, kind.Value, value);
    }
}
