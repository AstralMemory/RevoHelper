using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class GpuTelemetryFallbackTests
{
    // The two adapters of the development machine (2026-11-16), as Plug and Play lists them.
    private static readonly WindowsGpuAdapter Nvidia = new(
        @"PCI\VEN_10DE&DEV_2C18&SUBSYS_60401D05&REV_A1\ECD6428B002DB04800",
        "NVIDIA GeForce RTX 5090 Laptop GPU", HardwareVendor.Nvidia, DeviceKind.DiscreteGpu);

    private static readonly WindowsGpuAdapter Intel = new(
        @"PCI\VEN_8086&DEV_7D67&SUBSYS_50001D05&REV_06\3&11583659&0&10",
        "Intel(R) Graphics", HardwareVendor.Intel, DeviceKind.IntegratedGpu);

    private static readonly DateTimeOffset Start = new(2026, 11, 16, 22, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void AMachineWhereTheLibraryReadsEverythingIsNotAskedAnything()
    {
        var gpu = Source();
        IReadOnlyList<DeviceTelemetry> devices =
        [
            new(DeviceKind.Cpu, "CPU", 20, 64, 30, 4000),
            new(DeviceKind.DiscreteGpu, Nvidia.Name, 21, 53, 66, 1627, DedicatedMemoryMegabytes: 24463,
                Vendor: HardwareVendor.Nvidia, InstanceId: Nvidia.InstanceId),
            // No temperature for Intel's integrated GPU: its row shows the processor's, nothing is missing.
            new(DeviceKind.IntegratedGpu, Intel.Name, 37, null, 1.2, 550, Vendor: HardwareVendor.Intel, InstanceId: Intel.InstanceId)
        ];

        var result = new GpuTelemetryFallback(gpu).Fill(devices, Start);

        Assert.Same(devices, result);
        Assert.Equal(0, gpu.PowerReads + gpu.UsageReads + gpu.DriverReads + gpu.MemoryReads);
    }

    [Fact]
    public void AGpuTheLibraryDoesNotListGetsItsRowFromWindows()
    {
        var gpu = Source();
        var result = new GpuTelemetryFallback(gpu).Fill([new(DeviceKind.Cpu, "CPU", 20, 64, 30, 4000)], Start);

        var discrete = result.Single(device => device.InstanceId == Nvidia.InstanceId);
        Assert.Equal(DeviceKind.DiscreteGpu, discrete.Kind);
        Assert.Equal(HardwareVendor.Nvidia, discrete.Vendor);
        Assert.Equal(Nvidia.Name, discrete.Model);
        Assert.Equal(31, discrete.UsagePercent);
        Assert.Equal(53.8, discrete.TemperatureC);
        Assert.Equal(1627, discrete.FrequencyMhz);
        Assert.Equal(24136, discrete.DedicatedMemoryMegabytes);
        Assert.Null(discrete.PowerWatts);
        Assert.False(discrete.Asleep);

        var integrated = result.Single(device => device.InstanceId == Intel.InstanceId);
        Assert.Equal(DeviceKind.IntegratedGpu, integrated.Kind);
        Assert.Equal(12, integrated.UsagePercent);
        Assert.Equal(550, integrated.FrequencyMhz);
        Assert.Null(integrated.TemperatureC);
        Assert.Null(integrated.DedicatedMemoryMegabytes);
    }

    [Fact]
    public void OnlyWhatTheLibraryLeftEmptyIsFilled()
    {
        var gpu = Source();
        var library = new DeviceTelemetry(DeviceKind.DiscreteGpu, Nvidia.Name, null, 60, 80, null,
            DedicatedMemoryMegabytes: 24463, Vendor: HardwareVendor.Nvidia, InstanceId: Nvidia.InstanceId);

        var filled = new GpuTelemetryFallback(gpu).Fill([library], Start).Single(device => device.InstanceId == Nvidia.InstanceId);

        Assert.Equal(31, filled.UsagePercent);
        Assert.Equal(60, filled.TemperatureC);
        Assert.Equal(80, filled.PowerWatts);
        Assert.Equal(1627, filled.FrequencyMhz);
        Assert.Equal(24463, filled.DedicatedMemoryMegabytes);
        Assert.Equal(0, gpu.MemoryReads);
    }

    [Fact]
    public void WithoutAnInstanceIdTheSameNameOrTheOnlyDeviceOfItsVendorIsTheSameGpu()
    {
        IReadOnlyList<DeviceTelemetry> sameName = [new(DeviceKind.DiscreteGpu, "NVIDIA GeForce RTX 5090 Laptop GPU", 5, 50, 20, 900)];
        Assert.Equal([0, GpuTelemetryFallback.Added], GpuTelemetryFallback.Pair(sameName, [Nvidia, Intel]));

        IReadOnlyList<DeviceTelemetry> otherName =
            [new(DeviceKind.UnknownGpu, "GeForce RTX 5090 (driver name)", 5, 50, 20, 900, Vendor: HardwareVendor.Nvidia)];
        Assert.Equal([0, GpuTelemetryFallback.Added], GpuTelemetryFallback.Pair(otherName, [Nvidia, Intel]));

        IReadOnlyList<DeviceTelemetry> byPath =
        [
            new(DeviceKind.IntegratedGpu, "Intel GPU", 5, null, 1, 550, Vendor: HardwareVendor.Intel,
                InstanceId: @"\\?\PCI#VEN_8086&DEV_7D67&SUBSYS_50001D05&REV_06#3&11583659&0&10#{1ca05180-a699-450a-9a0c-de4fbe3ddd89}")
        ];
        Assert.Equal([GpuTelemetryFallback.Added, 0], GpuTelemetryFallback.Pair(byPath, [Nvidia, Intel]));
    }

    [Fact]
    public void AnUnmatchedLibraryDeviceOfTheSameVendorAddsNoSecondRow()
    {
        WindowsGpuAdapter integrated = new(@"PCI\VEN_1002&DEV_15BF&SUBSYS_00011D05&REV_C5\4&1", "AMD Radeon 780M Graphics",
            HardwareVendor.Amd, DeviceKind.IntegratedGpu);
        WindowsGpuAdapter discrete = new(@"PCI\VEN_1002&DEV_7480&SUBSYS_00011D05&REV_C1\4&2", "AMD Radeon RX 7600S",
            HardwareVendor.Amd, DeviceKind.DiscreteGpu);
        IReadOnlyList<DeviceTelemetry> devices = [new(DeviceKind.DiscreteGpu, "Radeon RX 7600S (library)", 5, 50, 20, 900, Vendor: HardwareVendor.Amd)];

        Assert.Equal([GpuTelemetryFallback.Unsure, GpuTelemetryFallback.Unsure], GpuTelemetryFallback.Pair(devices, [integrated, discrete]));

        var gpu = new FakeGpu();
        gpu.List.AddRange([integrated, discrete]);
        Assert.Same(devices, new GpuTelemetryFallback(gpu).Fill(devices, Start));
    }

    [Fact]
    public void APoweredDownGpuIsNotAskedAnythingAndShowsAsleep()
    {
        var gpu = Source();
        gpu.Power[Nvidia.InstanceId] = false;

        var result = new GpuTelemetryFallback(gpu).Fill([], Start);
        var discrete = result.Single(device => device.InstanceId == Nvidia.InstanceId);

        Assert.True(discrete.Asleep);
        Assert.Equal(0, discrete.UsagePercent);
        Assert.Equal(0, discrete.PowerWatts);
        Assert.Null(discrete.TemperatureC);
        Assert.Null(discrete.FrequencyMhz);
        Assert.DoesNotContain(Nvidia, gpu.DriverAsked);
        Assert.DoesNotContain(Nvidia, gpu.MemoryAsked);
        Assert.DoesNotContain(Nvidia, gpu.UsageAsked);
        var shown = TelemetryFormatter.Format(discrete);
        Assert.Equal("休眠", shown.Temperature);
        Assert.Equal("休眠", shown.Frequency);
    }

    [Fact]
    public void AnIdleDiscreteGpuIsLeftAloneForAWhileSoItCanPowerDown()
    {
        var gpu = Source();
        gpu.Usage[Nvidia.InstanceId] = 0;
        var fallback = new GpuTelemetryFallback(gpu);
        var now = Start;

        for (var tick = 0; tick < DiscreteGpuPollGate.BaseIdleSamples; tick++, now = now.AddSeconds(1))
            fallback.Fill([], now);
        Assert.Equal(DiscreteGpuPollGate.BaseIdleSamples, gpu.DriverAsked.Count(adapter => adapter == Nvidia));

        // Resting: the driver is not asked and the row keeps the last reading.
        var resting = fallback.Fill([], now).Single(device => device.InstanceId == Nvidia.InstanceId);
        Assert.Equal(DiscreteGpuPollGate.BaseIdleSamples, gpu.DriverAsked.Count(adapter => adapter == Nvidia));
        Assert.Equal(53.8, resting.TemperatureC);
        Assert.Equal(0, resting.UsagePercent);

        fallback.Fill([], now.Add(DiscreteGpuPollGate.Rest));
        Assert.Equal(DiscreteGpuPollGate.BaseIdleSamples + 1, gpu.DriverAsked.Count(adapter => adapter == Nvidia));
    }

    [Fact]
    public void AnIntegratedGpuIsAskedEachTimeWithoutResting()
    {
        var gpu = Source();
        gpu.Usage[Intel.InstanceId] = 0;
        gpu.Driver[Intel.InstanceId] = new(45, 550);
        var fallback = new GpuTelemetryFallback(gpu);

        var now = Start;
        IReadOnlyList<DeviceTelemetry> result = [];
        for (var tick = 0; tick < DiscreteGpuPollGate.BaseIdleSamples + 3; tick++, now = now.AddSeconds(1))
            result = fallback.Fill([], now);

        Assert.Equal(DiscreteGpuPollGate.BaseIdleSamples + 3, gpu.DriverAsked.Count(adapter => adapter == Intel));
        var integrated = result.Single(device => device.InstanceId == Intel.InstanceId);
        Assert.Equal(550, integrated.FrequencyMhz);
        // Its row shows the processor's temperature; the driver's figure is not needed.
        Assert.Null(integrated.TemperatureC);
    }

    // Intel's driver reported 0 Hz on every other read at 30 % load (2026-11-16): the engine was gated between frames.
    [Fact]
    public void AClockThatDropsOutForAMomentKeepsItsLastFigureBriefly()
    {
        var gpu = Source();
        var fallback = new GpuTelemetryFallback(gpu);
        Assert.Equal(550, IntegratedClock(fallback.Fill([], Start)));
        gpu.Driver[Intel.InstanceId] = WindowsGpuDriverReading.Empty;

        Assert.Equal(550, IntegratedClock(fallback.Fill([], Start.AddSeconds(1))));
        Assert.Equal(550, IntegratedClock(fallback.Fill([], Start.Add(GpuTelemetryFallback.Hold))));
        Assert.Null(IntegratedClock(fallback.Fill([], Start.Add(GpuTelemetryFallback.Hold).AddSeconds(1))));

        static double? IntegratedClock(IReadOnlyList<DeviceTelemetry> devices) =>
            devices.Single(device => device.InstanceId == Intel.InstanceId).FrequencyMhz;
    }

    [Fact]
    public void ADiscreteGpuIsNotAskedWhileItsPowerStateOrUsageIsUnknown()
    {
        var unknownPower = Source();
        unknownPower.Power[Nvidia.InstanceId] = null;
        var first = new GpuTelemetryFallback(unknownPower).Fill([], Start).Single(device => device.InstanceId == Nvidia.InstanceId);
        Assert.DoesNotContain(Nvidia, unknownPower.DriverAsked);
        Assert.DoesNotContain(Nvidia, unknownPower.MemoryAsked);
        Assert.Equal(31, first.UsagePercent);
        Assert.Null(first.TemperatureC);

        // Usage counters give their first rate only on the second read.
        var noUsageYet = Source();
        noUsageYet.Usage.Remove(Nvidia.InstanceId);
        new GpuTelemetryFallback(noUsageYet).Fill([], Start);
        Assert.DoesNotContain(Nvidia, noUsageYet.DriverAsked);
    }

    [Fact]
    public void AFailingSourceChangesNothing()
    {
        IReadOnlyList<DeviceTelemetry> devices = [new(DeviceKind.Cpu, "CPU", 20, 64, 30, 4000)];
        var broken = Source();
        broken.ThrowOnList = true;
        Assert.Same(devices, new GpuTelemetryFallback(broken).Fill(devices, Start));

        var driverFails = Source();
        driverFails.ThrowOnDriver = true;
        var discrete = new GpuTelemetryFallback(driverFails).Fill(devices, Start).Single(device => device.InstanceId == Nvidia.InstanceId);
        Assert.Equal(31, discrete.UsagePercent);
        Assert.Null(discrete.TemperatureC);
    }

    [Fact]
    public void WhatWindowsFilledIsLoggedOncePerAdapter()
    {
        var log = new List<string>();
        var fallback = new GpuTelemetryFallback(Source(), (area, message) => log.Add($"{area}: {message}"));

        fallback.Fill([], Start);
        fallback.Fill([], Start.AddSeconds(1));

        Assert.Equal(2, log.Count);
        Assert.Contains(log, line => line.StartsWith("telemetry-gpu: NVIDIA GeForce RTX 5090 Laptop GPU: not listed", StringComparison.Ordinal) &&
                                     line.Contains("usage, temperature, clock, memory", StringComparison.Ordinal));
    }

    [Fact]
    public void ALibraryDevicePathBecomesItsInstanceId()
    {
        Assert.Equal(Intel.InstanceId, PlugAndPlayDevices.NormalizeInstanceId(
            @"\\?\PCI#VEN_8086&DEV_7D67&SUBSYS_50001D05&REV_06#3&11583659&0&10#{1ca05180-a699-450a-9a0c-de4fbe3ddd89}"));
        Assert.Equal(Nvidia.InstanceId, PlugAndPlayDevices.NormalizeInstanceId(Nvidia.InstanceId));
    }

    private static FakeGpu Source()
    {
        var gpu = new FakeGpu();
        gpu.List.AddRange([Nvidia, Intel]);
        gpu.Usage[Nvidia.InstanceId] = 31;
        gpu.Usage[Intel.InstanceId] = 12;
        gpu.Driver[Nvidia.InstanceId] = new(53.8, 1627);
        gpu.Driver[Intel.InstanceId] = new(null, 550);
        gpu.Memory[Nvidia.InstanceId] = 24136;
        return gpu;
    }

    private sealed class FakeGpu : IWindowsGpuSource
    {
        public List<WindowsGpuAdapter> List { get; } = [];
        public Dictionary<string, bool?> Power { get; } = new();
        public Dictionary<string, double> Usage { get; } = new();
        public Dictionary<string, WindowsGpuDriverReading> Driver { get; } = new();
        public Dictionary<string, double> Memory { get; } = new();
        public List<WindowsGpuAdapter> DriverAsked { get; } = [];
        public List<WindowsGpuAdapter> MemoryAsked { get; } = [];
        public List<WindowsGpuAdapter> UsageAsked { get; } = [];
        public int PowerReads, UsageReads;
        public bool ThrowOnList, ThrowOnDriver;

        public int DriverReads => DriverAsked.Count;
        public int MemoryReads => MemoryAsked.Count;

        public IReadOnlyList<WindowsGpuAdapter> Adapters() =>
            ThrowOnList ? throw new InvalidOperationException("Plug and Play is unavailable.") : List;

        public bool? ReadPowered(WindowsGpuAdapter adapter)
        {
            PowerReads++;
            return Power.TryGetValue(adapter.InstanceId, out var powered) ? powered : true;
        }

        public IReadOnlyDictionary<string, double> ReadUsage(IReadOnlyList<WindowsGpuAdapter> adapters)
        {
            UsageReads++;
            UsageAsked.AddRange(adapters);
            return adapters.Where(adapter => Usage.ContainsKey(adapter.InstanceId))
                .ToDictionary(adapter => adapter.InstanceId, adapter => Usage[adapter.InstanceId]);
        }

        public WindowsGpuDriverReading ReadDriver(WindowsGpuAdapter adapter)
        {
            DriverAsked.Add(adapter);
            if (ThrowOnDriver) throw new InvalidOperationException("The driver did not answer.");
            return Driver.TryGetValue(adapter.InstanceId, out var reading) ? reading : WindowsGpuDriverReading.Empty;
        }

        public double? ReadDedicatedMemoryMegabytes(WindowsGpuAdapter adapter)
        {
            MemoryAsked.Add(adapter);
            return Memory.TryGetValue(adapter.InstanceId, out var megabytes) ? megabytes : null;
        }

        public void Dispose() { }
    }
}
