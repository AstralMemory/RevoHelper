using System.Reflection;
using LibreHardwareMonitor.Hardware;
using RevoHelper.Hardware.Windows.Telemetry;
using LhmHardware = LibreHardwareMonitor.Hardware.Hardware;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class LibreSensorHistoryTests
{
    [Fact]
    public void EnumeratingNewlyActiveSensorsKeepsCurrentReadingsWithoutRecordingHistory()
    {
        var settings = new MemorySettings();
        var hardware = new MemoryHardware(settings);
        var enumerate = typeof(LibreHardwareTelemetrySource)
            .GetMethod("EnumerateSensors", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<IHardware, IEnumerable<ISensor>>>();

        // Exercise the same enumeration a second time after another sensor becomes active.
        for (var index = 0; index < 2; index++)
        {
            var (sensor, publish) = hardware.AddSensor(index, settings);
            for (var value = 1; value <= 8; value++) publish(value);
            Assert.NotEmpty(sensor.Values);

            Assert.Contains(sensor, enumerate(hardware).ToArray());
            for (var value = 9; value <= 16; value++) publish(value);
            Assert.Empty(sensor.Values);
            Assert.Equal(16f, sensor.Value);
            Assert.Equal(1f, sensor.Min);
            Assert.Equal(16f, sensor.Max);
        }
    }

    private sealed class MemoryHardware(ISettings settings)
        : LhmHardware("Fixture", new Identifier("fixture"), settings)
    {
        public override HardwareType HardwareType => HardwareType.Cpu;
        public override void Update() => throw new NotSupportedException();

        public (ISensor Sensor, Action<float?> Publish) AddSensor(int index, ISettings sensorSettings)
        {
            // The library's concrete Sensor has no public type name. Use it to exercise its
            // real history implementation without opening a Computer or polling any device.
            var type = typeof(ISensor).Assembly.GetType("LibreHardwareMonitor.Hardware.Sensor", throwOnError: true)!;
            var sensor = (ISensor)Activator.CreateInstance(type, "Load", index, SensorType.Load, this, sensorSettings)!;
            ActivateSensor(sensor);
            return (sensor, type.GetProperty("Value")!.GetSetMethod(true)!.CreateDelegate<Action<float?>>(sensor));
        }
    }

    private sealed class MemorySettings : ISettings
    {
        public bool Contains(string name) => false;
        public string GetValue(string name, string value) => value;
        public void Remove(string name) { }
        public void SetValue(string name, string value) { }
    }
}
