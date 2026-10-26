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

    private static MainWindowViewModel Create(Func<DateTimeOffset> now) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "Test", "", "Test CPU", ""),
            ["NVIDIA GeForce RTX 5090 Laptop GPU"]), new(), new MemorySettings(), new NoStartup(), utcNow: now);

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
