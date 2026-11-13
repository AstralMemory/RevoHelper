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
    private readonly DiscreteGpuPollGate discreteGpuGate = new();
    private readonly Func<bool?> readDiscreteGpuPowered;
    private readonly IProcessorPerformanceReader processorPerformanceReader;
    private bool opened;
    private bool disposed;

    public LibreHardwareTelemetrySource(IProcessorPerformanceReader? processorPerformanceReader = null,
        Func<bool?>? readDiscreteGpuPowered = null)
    {
        this.readDiscreteGpuPowered = readDiscreteGpuPowered ?? new NvidiaDevicePowerProbe().ReadPowered;
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
        var now = DateTimeOffset.Now;
        // Asking the NVIDIA driver once a second keeps an idle GPU awake, so ask Windows first.
        var pollDiscrete = discreteGpuGate.ShouldPoll(readDiscreteGpuPowered(), now);
        UpdateComputer(pollDiscrete);
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

            if (hardware.HardwareType is HardwareType.GpuNvidia)
            {
                if (pollDiscrete) discreteGpuGate.Observe(selected.UsagePercent, now);
                // Powered down: the sensors still hold the last reading from before it slept.
                if (discreteGpuGate.Asleep)
                    selected = selected with { UsagePercent = 0, PowerWatts = 0, TemperatureC = null, FrequencyMhz = null };
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
        UpdateComputer(pollDiscreteGpu: true);
        return computer.Hardware
            .SelectMany(hardware => EnumerateSensors(hardware).Select(sensor => new HardwareSensorCatalogEntry(
                hardware.HardwareType.ToString(),
                hardware.Name,
                sensor.SensorType.ToString(),
                sensor.Name,
                sensor.Value)))
            .ToArray();
    }

    private void UpdateComputer(bool pollDiscreteGpu)
    {
        visitor.SkipNvidia = !pollDiscreteGpu;
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

        public bool SkipNvidia { get; set; }

        public void VisitHardware(IHardware hardware)
        {
            if (!(SkipNvidia && hardware.HardwareType is HardwareType.GpuNvidia)) hardware.Update();
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
