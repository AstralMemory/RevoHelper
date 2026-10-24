using LibreHardwareMonitor.Hardware;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Telemetry;

public sealed class LibreHardwareTelemetrySource : IHardwareTelemetrySource
{
    private readonly Computer computer = new()
    {
        IsCpuEnabled = true,
        IsGpuEnabled = true
    };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly UpdateVisitor visitor = new();
    private readonly IProcessorPerformanceReader processorPerformanceReader;
    private bool opened;
    private bool disposed;

    public LibreHardwareTelemetrySource(IProcessorPerformanceReader? processorPerformanceReader = null)
    {
        this.processorPerformanceReader = processorPerformanceReader ??
            new WindowsProcessorPerformanceReader(new RetryingWmiQuery(
                new SystemManagementWmiQuery(),
                maxAttempts: 3,
                delay: TimeSpan.FromMilliseconds(100)));
    }

    public async ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(ReadCore, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask<IReadOnlyList<HardwareSensorCatalogEntry>> ReadSensorCatalogAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(ReadSensorCatalogCore, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (opened)
            {
                computer.Close();
            }

            disposed = true;
        }
        finally
        {
            gate.Release();
            gate.Dispose();
        }
    }

    private HardwareTelemetrySnapshot ReadCore()
    {
        UpdateComputer();
        var devices = new List<DeviceTelemetry>();
        foreach (var hardware in computer.Hardware)
        {
            var kind = LibreHardwareKindClassifier.Classify(hardware.HardwareType, hardware.Name);
            if (kind is null)
            {
                continue;
            }

            var sensors = EnumerateSensors(hardware)
                .Select(ToDescriptor)
                .Where(descriptor => descriptor is not null)
                .Cast<SensorDescriptor>();
            var selected = kind.Value is Core.Dashboard.DeviceKind.Cpu
                ? LibreSensorSelector.SelectCpu(sensors)
                : LibreSensorSelector.SelectGpu(sensors);
            if (kind.Value is Core.Dashboard.DeviceKind.Cpu)
            {
                selected = ProcessorTelemetryFallback.ReadIfNeeded(selected, processorPerformanceReader);
            }

            devices.Add(new(
                kind.Value,
                hardware.Name,
                selected.UsagePercent,
                selected.TemperatureC,
                selected.PowerWatts,
                selected.FrequencyMhz,
                selected.PerformanceCoreFrequencyMhz,
                selected.EfficiencyCoreFrequencyMhz,
                selected.DedicatedMemoryMegabytes,
                selected.MaximumCoreUsagePercent));
        }

        return new(DateTimeOffset.Now, devices);
    }

    private IReadOnlyList<HardwareSensorCatalogEntry> ReadSensorCatalogCore()
    {
        UpdateComputer();
        return computer.Hardware
            .SelectMany(hardware => EnumerateSensors(hardware).Select(sensor => new HardwareSensorCatalogEntry(
                hardware.HardwareType.ToString(),
                hardware.Name,
                sensor.SensorType.ToString(),
                sensor.Name,
                sensor.Value)))
            .ToArray();
    }

    private void UpdateComputer()
    {
        if (!opened)
        {
            computer.Open();
            opened = true;
        }

        computer.Accept(visitor);
    }

    private static IEnumerable<ISensor> EnumerateSensors(IHardware hardware)
    {
        foreach (var sensor in hardware.Sensors)
        {
            yield return sensor;
        }

        foreach (var subHardware in hardware.SubHardware)
        {
            foreach (var sensor in EnumerateSensors(subHardware))
            {
                yield return sensor;
            }
        }
    }

    private static SensorDescriptor? ToDescriptor(ISensor sensor) =>
        LibreSensorDescriptorFactory.Create(sensor.Name, sensor.SensorType, sensor.Value);

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer target) => target.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
            {
                subHardware.Accept(this);
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}

public sealed record HardwareSensorCatalogEntry(
    string HardwareType,
    string HardwareName,
    string SensorType,
    string SensorName,
    double? Value);
