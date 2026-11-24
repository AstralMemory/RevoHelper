using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Gpu;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>
/// One reading per second. The sensor library comes first; what it cannot read is filled from
/// Windows' own counters, for the CPU and for every GPU, and if the library fails outright the rows
/// still come from Windows. A device the library cannot update is skipped on its own instead of
/// blanking the others.
/// </summary>
public sealed class LibreHardwareTelemetrySource : IHardwareTelemetrySource
{
    private static readonly TimeSpan LibraryRetryDelay = TimeSpan.FromSeconds(30);
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
    private readonly IWindowsCounterSource windowsCounters;
    private readonly IWindowsGpuSource windowsGpu;
    private readonly GpuTelemetryFallback gpuFallback;
    private readonly Func<string> readProcessorName;
    private readonly Action<string, string>? diagnostic;
    private string? processorName;
    private DateTimeOffset libraryRetryAt = DateTimeOffset.MinValue;
    private bool libraryFailing;
    private bool gpuFallbackFailing;
    private bool opened;
    private bool disposed;

    public LibreHardwareTelemetrySource(IProcessorPerformanceReader? processorPerformanceReader = null,
        Func<bool?>? readDiscreteGpuPowered = null,
        IWindowsCounterSource? windowsCounters = null,
        Func<string>? readProcessorName = null,
        Action<string, string>? diagnostic = null,
        IWindowsGpuSource? windowsGpu = null)
    {
        this.readDiscreteGpuPowered = readDiscreteGpuPowered ?? new NvidiaDevicePowerProbe().ReadPowered;
        this.processorPerformanceReader = processorPerformanceReader ??
            new WindowsProcessorPerformanceReader(new RetryingWmiQuery(
                new SystemManagementWmiQuery(),
                maxAttempts: 3,
                delay: TimeSpan.FromMilliseconds(100)));
        this.windowsCounters = windowsCounters ?? new PdhWindowsCounterSource();
        this.windowsGpu = windowsGpu ?? new WddmGpuSource();
        this.readProcessorName = readProcessorName ?? WindowsMachineIdentityReader.ReadRegistryProcessorName;
        this.diagnostic = diagnostic;
        gpuFallback = new GpuTelemetryFallback(this.windowsGpu, Report);
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
                try { computer.Close(); }
                catch (Exception) { /* Closing a library that already failed must not stop the exit. */ }
            }

            windowsCounters.Dispose();
            try { windowsGpu.Dispose(); }
            catch (Exception) { /* Same: closing never stops the exit. */ }
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
        IReadOnlyList<DeviceTelemetry> devices = [];
        if (now >= libraryRetryAt)
        {
            try
            {
                devices = ReadLibrary(now);
                if (libraryFailing) Report("telemetry-library", "recovered");
                libraryFailing = false;
            }
            catch (Exception exception)
            {
                // Without the library the rows still get Windows' readings; it is tried again later.
                libraryRetryAt = now + LibraryRetryDelay;
                if (!libraryFailing) Report("telemetry-library", $"{exception.GetType().Name}: {exception.Message}");
                libraryFailing = true;
                devices = [];
            }
        }

        // GPUs first: one the library cannot list still gets a row, and an Intel one then its
        // share of the processor's energy meter below.
        try
        {
            devices = gpuFallback.Fill(devices, now);
            gpuFallbackFailing = false;
        }
        catch (Exception exception)
        {
            if (!gpuFallbackFailing) Report("telemetry-gpu", $"{exception.GetType().Name}: {exception.Message}");
            gpuFallbackFailing = true;
        }

        if (ProcessorTelemetryFallback.NeedsWindowsCounters(devices))
        {
            WindowsCounterReading windows;
            try { windows = windowsCounters.Read(); }
            catch (Exception) { windows = WindowsCounterReading.Empty; }
            devices = ProcessorTelemetryFallback.Fill(devices, windows, ProcessorName(), processorPerformanceReader.Read);
        }

        return new(DateTimeOffset.Now, devices);
    }

    private List<DeviceTelemetry> ReadLibrary(DateTimeOffset now)
    {
        // Asking the NVIDIA driver once a second keeps an idle GPU awake, so ask Windows first.
        var pollDiscrete = discreteGpuGate.ShouldPoll(readDiscreteGpuPowered(), now);
        UpdateComputer(pollDiscrete);
        var devices = new List<DeviceTelemetry>();
        foreach (var hardware in computer.Hardware)
        {
            // Stale numbers from a device that failed to update are worse than none: its row expires instead.
            if (visitor.Failed.Contains(hardware)) continue;
            try
            {
                if (ReadDevice(hardware, pollDiscrete, now) is { } device) devices.Add(device);
            }
            catch (Exception exception)
            {
                Report("telemetry-device", $"{hardware.HardwareType} {hardware.Name}: {exception.GetType().Name}");
            }
        }

        return devices;
    }

    private DeviceTelemetry? ReadDevice(IHardware hardware, bool pollDiscrete, DateTimeOffset now)
    {
        var kind = LibreHardwareKindClassifier.Classify(hardware.HardwareType, hardware.Name, hardware.Identifier.ToString());
        if (kind is null)
        {
            return null;
        }

        var sensors = EnumerateSensors(hardware)
            .Select(ToDescriptor)
            .Where(descriptor => descriptor is not null)
            .Cast<SensorDescriptor>();
        var selected = kind.Value is Core.Dashboard.DeviceKind.Cpu
            ? LibreSensorSelector.SelectCpu(sensors)
            : LibreSensorSelector.SelectGpu(sensors);
        var asleep = false;
        if (hardware.HardwareType is HardwareType.GpuNvidia)
        {
            if (pollDiscrete) discreteGpuGate.Observe(selected.UsagePercent, now);
            // Powered down: the sensors still hold the last reading from before it slept.
            if (discreteGpuGate.Asleep)
            {
                selected = selected with { UsagePercent = 0, PowerWatts = 0, TemperatureC = null, FrequencyMhz = null };
                asleep = true;
            }
        }

        return new(
            kind.Value,
            hardware.Name,
            selected.UsagePercent,
            selected.TemperatureC,
            selected.PowerWatts,
            selected.FrequencyMhz,
            selected.PerformanceCoreFrequencyMhz,
            selected.EfficiencyCoreFrequencyMhz,
            selected.DedicatedMemoryMegabytes,
            selected.MaximumCoreUsagePercent,
            LibreHardwareKindClassifier.Vendor(hardware.HardwareType),
            asleep,
            InstanceId: GpuInstanceId(hardware));
    }

    /// <summary>The library knows which Plug and Play device each GPU is; that pairs it with Windows' adapter.</summary>
    private static string? GpuInstanceId(IHardware hardware)
    {
        try
        {
            return hardware is GenericGpu { DeviceId: { Length: > 0 } id } ? PlugAndPlayDevices.NormalizeInstanceId(id) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string ProcessorName()
    {
        if (processorName is not null) return processorName;
        try { processorName = readProcessorName(); }
        catch (Exception) { processorName = ""; }
        return processorName ??= "";
    }

    private void Report(string area, string message)
    {
        try { diagnostic?.Invoke(area, message); }
        catch (Exception) { /* Logging never changes a reading. */ }
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
        visitor.Failed.Clear();
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
            // The dashboard and diagnostic catalog only read live values. Configuring each
            // sensor here also covers ones that become active after startup.
            if (sensor.ValuesTimeWindow != TimeSpan.Zero)
                sensor.ValuesTimeWindow = TimeSpan.Zero;
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

        /// <summary>Devices whose update threw this tick; the others still update.</summary>
        public HashSet<IHardware> Failed { get; } = new(ReferenceEqualityComparer.Instance);

        public void VisitHardware(IHardware hardware)
        {
            try
            {
                if (!(SkipNvidia && hardware.HardwareType is HardwareType.GpuNvidia)) hardware.Update();
            }
            catch (Exception) { Failed.Add(hardware); }

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
