using RevoHelper.App.Presentation;
using RevoHelper.App.Settings;
using RevoHelper.App.SystemControl;
using RevoHelper.Core.Control;
using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.App.Tests.Presentation;

public sealed class KeyboardLightingCompatibilityTests
{
    [Fact]
    public async Task LightingDrawer_DiscoversLegacyPowerWithoutAdvertisingRgbConfiguration()
    {
        var channel = new FakeChannel();
        var vm = Create(channel);

        await vm.OpenLightingAsync(default);

        Assert.True(vm.IsKeyboardLightingZoneAvailable);
        Assert.True(vm.IsKeyboardLightingEnabled);
        Assert.True(vm.CanToggleSelectedLightingZonePower);
        Assert.False(vm.CanApplyLighting);
        Assert.False(vm.CanApplyLightingSleepTimer);
        Assert.Equal(LightingEffect.OemDefault, vm.SelectedLightingEffect);
        Assert.Equal(1, channel.LegacyReads);
        Assert.Empty(channel.LegacyWrites);
        Assert.Empty(channel.ZoneWrites);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SolePowerSwitch_UsesOneLegacyWriteAndConfirmedReadback(bool enabled)
    {
        var channel = new FakeChannel { Enabled = !enabled };
        var vm = Create(channel);
        await vm.OpenLightingAsync(default);
        vm.IsSelectedLightingZoneEnabled = enabled;

        await vm.SetSelectedLightingZonePowerAsync(default);

        Assert.Equal(GcuQuickControlsCommand.KeyboardBacklight(enabled), Assert.Single(channel.LegacyWrites));
        Assert.Empty(channel.ZoneWrites);
        Assert.Equal(enabled, vm.IsKeyboardLightingEnabled);
        Assert.Equal(enabled, vm.IsSelectedLightingZoneEnabled);
        Assert.True(vm.CanToggleSelectedLightingZonePower);
        Assert.False(vm.CanApplyLighting);
        Assert.False(vm.IsLightingBusy);
    }

    [Fact]
    public async Task CompatibilityApi_DelegatesToSameSingleWritePath()
    {
        var channel = new FakeChannel();
        var vm = Create(channel);

        await vm.SetKeyboardLightingAsync(false, default);

        Assert.Single(channel.LegacyWrites);
        Assert.Empty(channel.ZoneWrites);
        Assert.False(vm.IsKeyboardLightingEnabled);
        Assert.False(vm.CanApplyLighting);
    }

    [Fact]
    public async Task LegacyPowerReadbackMismatch_DoesNotClaimSuccessOrIssueSecondWrite()
    {
        var channel = new FakeChannel { IgnoreWrites = true };
        var vm = Create(channel);
        await vm.OpenLightingAsync(default);
        vm.IsSelectedLightingZoneEnabled = false;

        await vm.SetSelectedLightingZonePowerAsync(default);

        Assert.Single(channel.LegacyWrites);
        Assert.Empty(channel.ZoneWrites);
        Assert.True(vm.IsKeyboardLightingEnabled);
        Assert.True(vm.IsSelectedLightingZoneEnabled);
        Assert.Contains("未得到目标状态回读", vm.KeyboardControlsStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyPowerOnly_ConfigurationAndPresetsDoNotWriteRgb()
    {
        var channel = new FakeChannel();
        var vm = Create(channel);
        await vm.OpenLightingAsync(default);
        vm.SelectedLightingEffect = LightingEffect.Static;
        vm.LightingColorHex = "#FF3300";
        vm.LightingBrightness = 4;

        await vm.ApplySelectedLightingZoneAsync(default);
        await vm.ApplyLightingProfileAsync(LightingProfileCatalog.BuiltIns[0], default);
        await vm.ApplyOemDefaultLightingAsync(default);

        Assert.False(vm.CanApplyLighting);
        Assert.Empty(channel.LegacyWrites);
        Assert.Empty(channel.ZoneWrites);
    }

    [Fact]
    public async Task MissingLegacyStatus_DoesNotInventPowerCapability()
    {
        var channel = new FakeChannel { MissingLegacyStatus = true };
        var vm = Create(channel);

        await vm.OpenLightingAsync(default);

        Assert.False(vm.IsKeyboardLightingZoneAvailable);
        Assert.False(vm.CanToggleSelectedLightingZonePower);
        Assert.False(vm.CanApplyLighting);
        Assert.Empty(channel.LegacyWrites);
        Assert.Empty(channel.ZoneWrites);
    }

    [Fact]
    public async Task DetailedRgbKeyboard_RetainsZonePowerPathWithoutLegacyDuplicate()
    {
        var channel = new FakeChannel { DetailedZone = true };
        var vm = Create(channel);
        await vm.OpenLightingAsync(default);
        Assert.True(vm.CanApplyLighting);
        vm.IsSelectedLightingZoneEnabled = false;

        await vm.SetSelectedLightingZonePowerAsync(default);

        Assert.Equal(0, channel.LegacyReads);
        Assert.Empty(channel.LegacyWrites);
        Assert.Equal(GcuQuickControlsCommand.KeyboardBacklight(false), Assert.Single(channel.ZoneWrites));
        Assert.False(vm.IsKeyboardLightingEnabled);
        Assert.True(vm.CanApplyLighting);
    }

    private static MainWindowViewModel Create(FakeChannel channel) => new(
        HomeDashboardFactory.Create(new MachineIdentity("MECHREVO", "fake", "fake", "fake", "fake"), []),
        new(), new Settings(), new Startup(),
        quickControlsController: new GcuQuickControlsController(channel, (_, _) => Task.CompletedTask),
        lightingProfileStore: new MemoryLightingProfileStore());

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
        public bool Enabled { get; set; } = true;
        public bool DetailedZone { get; init; }
        public bool MissingLegacyStatus { get; init; }
        public bool IgnoreWrites { get; init; }
        public int LegacyReads { get; private set; }
        public List<string> LegacyWrites { get; } = [];
        public List<string> ZoneWrites { get; } = [];
        public ValueTask<string> ReadKeyboardStatusAsync(CancellationToken token)
        {
            LegacyReads++;
            return ValueTask.FromResult(MissingLegacyStatus ? "{}" :
                $$"""{"powerStatus":"{{(Enabled ? "On" : "Off")}}","type":"SingleZone"}""");
        }
        public ValueTask<string> ReadLightingStatusAsync(LightingZone zone, CancellationToken token) =>
            ValueTask.FromResult(zone == LightingZone.Keyboard && DetailedZone
                ? $$"""{"powerStatus":"{{(Enabled ? "On" : "Off")}}","type":"FourZone","effect":"Static","light":3,"speed":2,"direction":"Left","R":128,"G":0,"B":255}"""
                : "{}");
        public ValueTask WriteKeyboardAsync(string payload, CancellationToken token)
        {
            LegacyWrites.Add(payload);
            ApplyPower(payload);
            return ValueTask.CompletedTask;
        }
        public ValueTask<string> WriteLightingAndReadStatusAsync(LightingZone zone, string payload, CancellationToken token)
        {
            ZoneWrites.Add(payload);
            ApplyPower(payload);
            return ReadLightingStatusAsync(zone, token);
        }
        private void ApplyPower(string payload)
        {
            if (!IgnoreWrites && payload == GcuQuickControlsCommand.KeyboardBacklight(true)) Enabled = true;
            if (!IgnoreWrites && payload == GcuQuickControlsCommand.KeyboardBacklight(false)) Enabled = false;
        }
        public ValueTask WriteLightingAsync(LightingZone zone, string payload, CancellationToken token)
        {
            ZoneWrites.Add(payload);
            ApplyPower(payload);
            return ValueTask.CompletedTask;
        }
        public ValueTask<string> ReadSettingStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<string> ReadDeviceSwitchStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<string> ReadBatteryProtectionAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<string> ReadLiquidCoolingAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask<int?> ReadBatteryChargeLimitAsync(CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteSettingAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteBatteryProtectionAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteLiquidCoolingAsync(string payload, CancellationToken token) => throw new NotSupportedException();
        public ValueTask WriteBatteryChargeLimitAsync(string payload, CancellationToken token) => throw new NotSupportedException();
    }
}
