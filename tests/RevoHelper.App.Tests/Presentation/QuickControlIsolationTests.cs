using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.SystemControl;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.App.Tests.Presentation;

public sealed class QuickControlIsolationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatteryDrawer_IndependentFailuresDoNotHideWorkingPowerControls(bool protectionFails)
    {
        var channel = new FakeChannel
        {
            BatteryError = protectionFails ? new TimeoutException("battery topic timeout") : null,
            ChargeLimitError = protectionFails ? null : new TimeoutException("charge limit timeout")
        };
        var vm = Create(channel);

        await vm.OpenBatteryAsync(default);

        Assert.True(vm.SupportsUsbChargingWhileOff);
        Assert.True(vm.SupportsDeepSleep);
        Assert.True(vm.IsUsbChargingWhileOffEnabled);
        Assert.Contains("部分", vm.BatteryStatusText, StringComparison.Ordinal);
        Assert.Contains(protectionFails ? "battery topic timeout" : "charge limit timeout", vm.BatteryStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatteryDrawer_PartialRefreshRetainsOtherPreviouslyConfirmedFields()
    {
        var channel = new FakeChannel();
        var vm = Create(channel);
        await vm.OpenBatteryAsync(default);
        channel.SettingError = new TimeoutException("setting timeout");
        channel.ChargeLimit = 70;

        await vm.OpenBatteryAsync(default);

        Assert.True(vm.SupportsUsbChargingWhileOff);
        Assert.True(vm.IsUsbChargingWhileOffEnabled);
        Assert.True(vm.SupportsDeepSleep);
        Assert.True(vm.SupportsAcAutoBoot);
        Assert.True(vm.SupportsBatteryChargeLimit);
        Assert.Equal(70, vm.BatteryChargeLimitPercent);
        Assert.Contains("setting timeout", vm.BatteryStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyboardDrawer_SettingFailureDoesNotPreventDeviceReadOrClearPreviousFields()
    {
        var channel = new FakeChannel();
        var vm = Create(channel);
        await vm.OpenKeyboardControlsAsync(default);
        channel.SettingError = new TimeoutException("setting timeout");
        channel.DevicePayload = "{\"WIFIEnable\":false}";

        await vm.OpenKeyboardControlsAsync(default);

        Assert.True(vm.SupportsWifiToggle);
        Assert.False(vm.IsWifiEnabled);
        Assert.True(vm.SupportsCopilotKeyToggle);
        Assert.True(vm.IsCopilotKeyEnabled);
        Assert.Contains("部分", vm.KeyboardControlsStatusText, StringComparison.Ordinal);
        Assert.Contains("setting timeout", vm.KeyboardControlsStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeyboardDrawer_DeviceFailureRetainsDeviceFieldsAndReadsSetting()
    {
        var channel = new FakeChannel();
        var vm = Create(channel);
        await vm.OpenKeyboardControlsAsync(default);
        channel.DeviceError = new TimeoutException("device timeout");
        channel.CopilotEnabled = false;

        await vm.OpenKeyboardControlsAsync(default);

        Assert.True(vm.SupportsWifiToggle);
        Assert.True(vm.IsWifiEnabled);
        Assert.True(vm.SupportsCopilotKeyToggle);
        Assert.False(vm.IsCopilotKeyEnabled);
        Assert.Contains("device timeout", vm.KeyboardControlsStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatteryDrawer_AllReadFailuresAreReportedAndCancellationPropagates()
    {
        var channel = new FakeChannel
        {
            BatteryError = new TimeoutException("battery topic timeout"),
            SettingError = new TimeoutException("setting timeout"),
            ChargeLimitError = new TimeoutException("charge limit timeout")
        };
        var vm = Create(channel);
        await vm.OpenBatteryAsync(default);
        Assert.False(vm.SupportsUsbChargingWhileOff);
        Assert.Contains("失败", vm.BatteryStatusText, StringComparison.Ordinal);
        Assert.Contains("battery topic timeout", vm.BatteryStatusText, StringComparison.Ordinal);
        Assert.Contains("setting timeout", vm.BatteryStatusText, StringComparison.Ordinal);
        channel.BatteryError = new OperationCanceledException();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => vm.OpenBatteryAsync(default));
    }

    [Fact]
    public async Task BatteryDrawer_EmptySurvivingChannelIsNotPresentedAsPartialSuccess()
    {
        var channel = new FakeChannel
        {
            BatteryError = new TimeoutException("battery topic timeout"),
            SettingError = new TimeoutException("setting timeout"),
            ChargeLimit = null
        };
        var vm = Create(channel);

        await vm.OpenBatteryAsync(default);

        Assert.StartsWith("电源与电池状态读取失败：", vm.BatteryStatusText, StringComparison.Ordinal);
        Assert.False(vm.SupportsBatteryChargeLimit);
        Assert.False(vm.SupportsUsbChargingWhileOff);
    }

    private static MainWindowViewModel Create(FakeChannel channel) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "fake", "fake", "fake", "fake"), []),
        new(), new Settings(), new Startup(),
        quickControlsController: new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask));

    private sealed class Settings : IApplicationSettingsStore
    {
        public ApplicationSettings Load() => new();
        public void Save(ApplicationSettings settings) => throw new InvalidOperationException("Unexpected save");
    }

    private sealed class Startup : IStartupRegistration
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) => throw new InvalidOperationException("Unexpected system write");
    }

    private sealed class FakeChannel : IGcuQuickControlsChannel
    {
        public Exception? BatteryError { get; set; }
        public Exception? SettingError { get; set; }
        public Exception? DeviceError { get; set; }
        public Exception? ChargeLimitError { get; set; }
        public bool CopilotEnabled { get; set; } = true;
        public int? ChargeLimit { get; set; } = 80;
        public string DevicePayload { get; set; } = "{\"WIFIEnable\":true}";
        public ValueTask<string> ReadSettingStatusAsync(CancellationToken token) => SettingError is { } error
            ? ValueTask.FromException<string>(error)
            : ValueTask.FromResult("""{"WinKey":"WINKEY_UNLOCK","FnKey":"FNKEY_UNLOCK","OSD":"OSD_HIDDEN_OFF","CopilotKey":"COPILOTKEY_UNLOCK","UsbCharger":true,"AcRecoverySwitch_Support":true,"AcRecoverySwitch_Status":true,"DeepSleepSwitch":true,"DeepSleepTime":30}"""
                .Replace("COPILOTKEY_UNLOCK", CopilotEnabled ? "COPILOTKEY_UNLOCK" : "COPILOTKEY_LOCK", StringComparison.Ordinal));
        public ValueTask<string> ReadDeviceSwitchStatusAsync(CancellationToken token) => DeviceError is { } error
            ? ValueTask.FromException<string>(error) : ValueTask.FromResult(DevicePayload);
        public ValueTask<string> ReadKeyboardStatusAsync(CancellationToken token) => ValueTask.FromResult("{\"powerStatus\":\"On\"}");
        public ValueTask<string> ReadBatteryProtectionAsync(CancellationToken token) => BatteryError is { } error
            ? ValueTask.FromException<string>(error) : ValueTask.FromResult("{\"HealthProtectionStatus\":2,\"BatteryPercent\":70}");
        public ValueTask<int?> ReadBatteryChargeLimitAsync(CancellationToken token) => ChargeLimitError is { } error
            ? ValueTask.FromException<int?>(error) : ValueTask.FromResult(ChargeLimit);
        public ValueTask<string> ReadLightingStatusAsync(LightingZone zone, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<string> ReadLiquidCoolingAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteSettingAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteKeyboardAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteLightingAsync(LightingZone zone, string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask<string> WriteLightingAndReadStatusAsync(LightingZone zone, string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteBatteryProtectionAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteLiquidCoolingAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteBatteryChargeLimitAsync(string payload, CancellationToken token) => throw new NotSupportedException();
    }
}
