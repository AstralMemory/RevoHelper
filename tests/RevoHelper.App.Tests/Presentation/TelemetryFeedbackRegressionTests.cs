using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.Lifecycle;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.App.Tests.Presentation;

public sealed class TelemetryFeedbackRegressionTests
{
    [Fact]
    public void OperationFeedbackSurvivesBothTelemetrySuccessAndFailure()
    {
        var vm = Create(() => DateTimeOffset.UtcNow);
        vm.SelectProfile("office");
        var feedback = vm.StatusMessage;
        vm.ApplyTelemetry(new(DateTimeOffset.UtcNow, []));
        Assert.Equal(feedback, vm.StatusMessage);
        vm.ReportTelemetryUnavailable("offline");
        Assert.Equal(feedback, vm.StatusMessage);
    }

    [Fact]
    public void MissingDeviceExpiresWithoutLosingIdentityAndRecoversOnNewSample()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = Create(() => now);
        var gpu = vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu);
        var sample = new DeviceTelemetry(DeviceKind.DiscreteGpu, gpu.Model, 90, 70, 170, 2200);
        vm.ApplyTelemetry(new(now, [sample]));
        vm.ApplyTelemetry(new(now.AddSeconds(2), []));
        Assert.Equal("170 W", vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu).Power);
        now = now.AddSeconds(5);
        vm.ApplyTelemetry(new(now, []));
        var stale = vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu);
        Assert.Equal(gpu.Model, stale.Model);
        Assert.Equal("N/A", stale.Power);
        Assert.Equal("N/A", stale.Usage);
        vm.ApplyTelemetry(new(now.AddSeconds(1), [sample with { PowerWatts = 50 }]));
        Assert.Equal("50 W", vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu).Power);
    }

    [Fact]
    public void CollectorOutageExpiresRowsAndOemTemperatureFallback()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = Create(() => now);
        var cpu = vm.Devices.Single(d => d.Kind == DeviceKind.Cpu);
        vm.ApplyOemCpuStatus(new(60, null, null));
        vm.ApplyTelemetry(new(now, [new(DeviceKind.Cpu, cpu.Model, 30, null, 40, 3000)]));
        Assert.Equal("60°C", vm.Devices.Single(d => d.Kind == DeviceKind.Cpu).Temperature);
        now = now.AddSeconds(6);
        vm.ReportTelemetryUnavailable("offline");
        var stale = vm.Devices.Single(d => d.Kind == DeviceKind.Cpu);
        Assert.Equal("N/A", stale.Power);
        Assert.Equal("N/A", stale.Temperature);
        vm.ApplyTelemetry(new(now, [new(DeviceKind.Cpu, cpu.Model, 30, null, 40, 3000)]));
        Assert.Equal("N/A", vm.Devices.Single(d => d.Kind == DeviceKind.Cpu).Temperature);
    }

    // V1.0.7: sensors first, then the OEM service, then the firmware's thermal zone.
    [Fact]
    public void TheCpuTemperatureFallsBackToTheOemServiceAndThenTheThermalZone()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = Create(() => now);
        var cpu = vm.Devices.Single(d => d.Kind == DeviceKind.Cpu);
        var withoutSensor = new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 30, null, 40, 3000, ZoneTemperatureC: 66.1);

        vm.ApplyTelemetry(new(now, [withoutSensor]));
        Assert.Equal("66°C", vm.Devices.Single(d => d.Kind == DeviceKind.Cpu).Temperature);

        vm.ApplyOemCpuStatus(new(60, null, null));
        vm.ApplyTelemetry(new(now, [withoutSensor]));
        Assert.Equal("60°C", vm.Devices.Single(d => d.Kind == DeviceKind.Cpu).Temperature);

        vm.ApplyTelemetry(new(now, [withoutSensor with { TemperatureC = 71 }]));
        Assert.Equal("71°C", vm.Devices.Single(d => d.Kind == DeviceKind.Cpu).Temperature);
    }

    [Fact]
    public void AGpuWhoseNameAndKindDifferFromWindowsStillFindsItsRowByVendor()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = CreateFromAdapters(() => now);

        vm.ApplyTelemetry(new(now, [new DeviceTelemetry(DeviceKind.UnknownGpu, "NVIDIA RTX (driver name)", 41, 52, 60, 1800,
            Vendor: HardwareVendor.Nvidia)]));

        var row = vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu);
        Assert.Equal("41%", row.Usage);
        Assert.Equal("52°C", row.Temperature);
        Assert.Equal("N/A", vm.Devices.Single(d => d.Kind == DeviceKind.IntegratedGpu).Usage);
    }

    [Fact]
    public void TheIntegratedGpuRowSharesTheCpuTemperatureEvenWhenTheSensorsGuessedOtherwise()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = CreateFromAdapters(() => now);
        var cpu = vm.Devices.Single(d => d.Kind == DeviceKind.Cpu);
        var integrated = vm.Devices.Single(d => d.Kind == DeviceKind.IntegratedGpu);

        vm.ApplyTelemetry(new(now,
        [
            new DeviceTelemetry(DeviceKind.Cpu, cpu.Model, 20, 64, 30, 4000),
            // The library's name rule thought "Arc B390" was discrete; the PCI position says integrated.
            new DeviceTelemetry(DeviceKind.DiscreteGpu, "Intel(R) Arc(TM) B390 GPU", 3, null, 0.4, 900, Vendor: HardwareVendor.Intel)
        ]));

        var row = vm.Devices.Single(d => d.Model == integrated.Model);
        Assert.Equal("3%", row.Usage);
        Assert.Equal("64°C", row.Temperature);
    }

    [Fact]
    public void ASleepingDiscreteGpuSaysAsleepInsteadOfNA()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = Create(() => now);
        var gpu = vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu);

        vm.ApplyTelemetry(new(now, [new DeviceTelemetry(DeviceKind.DiscreteGpu, gpu.Model, 0, null, 0, null, Asleep: true)]));

        var row = vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu);
        Assert.Equal("休眠", row.Temperature);
        Assert.Equal("休眠", row.Frequency);
        Assert.Equal("0%", row.Usage);
    }

    [Fact]
    public void AGpuOnlyWindowsCouldReadStillFillsItsRow()
    {
        var now = DateTimeOffset.UtcNow;
        var vm = CreateFromAdapters(() => now);

        // The sensor library listed no GPU; Windows' own data has usage, temperature, clock and memory, but no power.
        vm.ApplyTelemetry(new(now, [new DeviceTelemetry(DeviceKind.DiscreteGpu, "NVIDIA GeForce RTX 5090 Laptop GPU", 31, 53.8, null, 1627,
            DedicatedMemoryMegabytes: 24136, Vendor: HardwareVendor.Nvidia,
            InstanceId: @"PCI\VEN_10DE&DEV_2C18&SUBSYS_60401D05&REV_A1\ECD6428B002DB04800")]));

        var row = vm.Devices.Single(d => d.Kind == DeviceKind.DiscreteGpu);
        Assert.Equal("31%", row.Usage);
        Assert.Equal("54°C", row.Temperature);
        Assert.Equal("1627 MHz", row.Frequency);
        Assert.Equal("N/A", row.Power);
        Assert.Equal("24 GB 显存", row.Details);
    }

    private static MainWindowViewModel Create(Func<DateTimeOffset> now) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "Test", "", "Test CPU", ""),
            ["NVIDIA GeForce RTX 5090 Laptop GPU"]), new(), new MemorySettings(), new NoStartup(), utcNow: now);

    private static MainWindowViewModel CreateFromAdapters(Func<DateTimeOffset> now) => new(
        HomeDashboardFactory.CreateFromAdapters(new MachineIdentity("MECHREVO", "Test", "", "Test CPU", ""),
        [
            new DisplayAdapter("Intel(R) Arc(TM) B390 GPU", HardwareVendor.Intel, Integrated: true),
            new DisplayAdapter("NVIDIA GeForce RTX 5090 Laptop GPU", HardwareVendor.Nvidia)
        ]), new(), new MemorySettings(), new NoStartup(), utcNow: now);

    private sealed class MemorySettings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) { }
    }
    private sealed class NoStartup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
}
