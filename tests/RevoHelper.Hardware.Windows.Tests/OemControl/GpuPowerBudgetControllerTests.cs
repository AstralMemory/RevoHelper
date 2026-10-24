using System.Text.Json.Nodes;
using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GpuPowerBudgetControllerTests
{
    [Fact]
    public async Task DynamicBoostSwitchOff_WritesOnlySwitchAndKeepsConfirmedOffWhenDriverDiffers()
    {
        var device = new Device { Tgp = 95, MinimumAcceptedTgp = 95 };
        var store = new Store();
        var controller = new GpuPowerBudgetController(device,
            _ => ValueTask.FromResult<PlatformModeSelection?>(device.Mode),
            _ => ValueTask.FromResult<double?>(100), store, (_, _) => Task.CompletedTask);

        var result = await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.False(device.BoostEnabled);
        Assert.Equal(95, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false), Assert.Single(device.Commands));
        Assert.Contains("100 W", result.Reason);
        Assert.DoesNotContain("总功率上限已确认 95 W", result.Reason);
    }

    [Fact]
    public async Task DynamicBoostSwitchOff_DriverUnavailableDoesNotUndoOemConfirmedSwitch()
    {
        var device = new Device();
        var controller = new GpuPowerBudgetController(device,
            _ => ValueTask.FromResult<PlatformModeSelection?>(device.Mode),
            _ => ValueTask.FromResult<double?>(null), new Store(), (_, _) => Task.CompletedTask);

        var result = await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.False(device.BoostEnabled);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false), Assert.Single(device.Commands));
        Assert.Contains("不可读", result.Reason);
        Assert.Contains("未确认", result.Reason);
    }

    [Fact]
    public async Task DynamicBoostSwitchOff_OemRejectionDoesNotRewriteUnrelatedFields()
    {
        var device = new Device { IgnoreBoostSwitch = true };
        var result = await Create(device, new Store()).ApplyAsync(
            CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);

        Assert.False(result.Succeeded);
        Assert.True(device.BoostEnabled);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false), Assert.Single(device.Commands));
    }

    [Fact]
    public async Task DynamicBoostSwitchOn_DiscardsOldCompensatedDisplayTargetWithoutRaisingActualTgp()
    {
        var device = new Device { Tgp = 145, BoostWatts = 0, BoostEnabled = false };
        var store = new Store();
        store.Write(1, new(150, 25, 145, 0, false));
        var budget = Create(device, store);
        var result = await budget.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 1, default);
        var controller = new GcuCustomPerformanceController(device, Path.GetTempPath(), powerBudget: budget);
        var actual = await controller.ReadGpuAsync(default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(true), Assert.Single(device.Commands));
        Assert.Equal(145, device.Tgp);
        Assert.NotNull(actual);
        Assert.Equal(145, actual.GpuConfigurableTgpWatts);
    }

    [Fact]
    public async Task DynamicBoostSwitchOff_ProfileChangeNeverRestoresOrContinuesWriting()
    {
        var device = new Device { ChangeProfileAfterWrite = true };
        var store = new Store();
        var result = await Create(device, store).ApplyAsync(
            CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);

        Assert.False(result.Succeeded);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false), Assert.Single(device.Commands));
        Assert.Null(store.Read(1));
    }

    [Fact]
    public async Task LowPowerTarget_ConfirmsOnlyOemAcceptedSeventyWattsWithoutInventingCompensation()
    {
        var device = new Device { BoostEnabled = false, BoostWatts = 0 };
        var store = new Store();
        var result = await Create(device, store).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(75, device.Ceiling);
        Assert.Equal(70, device.Tgp);
        Assert.False(device.BoostEnabled);
        Assert.Null(store.Read(1));
        Assert.Equal(GcuCustomPerformanceCommand.GpuConfigurableTgp(70), Assert.Single(device.Commands));
        Assert.Contains("75 W", result.Reason);
        Assert.All(device.Commands, command => Assert.DoesNotContain("CPU", command));
        Assert.All(device.Commands, command => Assert.DoesNotContain("ClockOffset", command));
    }

    [Fact]
    public async Task LowPowerTarget_FirmwareClampRestoresInsteadOfClaimingSeventyWatts()
    {
        var device = new Device { BoostEnabled = false, BoostWatts = 0, MinimumAcceptedTgp = 95 };
        var store = new Store();
        var result = await Create(device, store).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);

        Assert.False(result.Succeeded);
        Assert.Equal(150, device.Tgp);
        Assert.False(device.BoostEnabled);
        Assert.Null(store.Read(1));
        Assert.Contains("已恢复", result.Reason);
    }

    [Fact]
    public async Task LowPowerTarget_FirmwareClampReportsRequestedActualTgpAndDriverCeiling()
    {
        var device = new Device { BoostEnabled = false, BoostWatts = 0, MinimumAcceptedTgp = 95 };
        var result = await Create(device, new Store()).ApplyAsync(
            CustomPerformanceSetting.GpuTgpWatts, 70, default);

        Assert.False(result.Succeeded);
        Assert.Contains("70 W", result.Reason);
        Assert.Contains("95 W", result.Reason);
        Assert.Contains("100 W", result.Reason);
        Assert.Equal(150, device.Tgp);
    }

    [Theory]
    [InlineData(25)]
    [InlineData(69)]
    [InlineData(151)]
    public async Task LowPowerTarget_StillRejectsOutsideAdvertisedExtendedInputRange(int target)
    {
        var device = new Device();
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, target, default);
        Assert.False(result.Succeeded);
        Assert.Empty(device.Commands);
    }

    [Fact]
    public async Task LowPowerTarget_ProfileChangeNeverRestoresIntoAnotherPreset()
    {
        var device = new Device { BoostEnabled = false, BoostWatts = 0, ChangeProfileAfterWrite = true };
        var store = new Store();
        var result = await Create(device, store).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);
        Assert.False(result.Succeeded);
        Assert.Single(device.Commands);
        Assert.Null(store.Read(1));
    }

    [Fact]
    public async Task OffAndOn_PreserveRawTgpAndStoredBoostTierInsteadOfCompensatingEither()
    {
        var device = new Device();
        var store = new Store();
        var controller = Create(device, store);
        var off = await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);
        Assert.True(off.Succeeded, off.Reason);
        Assert.Equal(155, device.Ceiling);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.Null(store.Read(1));

        var on = await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 1, default);
        Assert.True(on.Succeeded, on.Reason);
        Assert.Equal(175, device.Ceiling);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.Equal(new[] { GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false),
            GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(true) }, device.Commands);
        Assert.All(device.Commands, command => Assert.DoesNotContain("CPU", command));
        Assert.All(device.Commands, command => Assert.DoesNotContain("ClockOffset", command));
    }

    [Fact]
    public async Task LowerBoostTier_ChangesOnlyOemTierAndReportsUnchangedDriverCeiling()
    {
        var device = new Device();
        var store = new Store();
        var result = await Create(device, store).ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostWatts, 5, default);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(175, device.Ceiling);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(5, device.BoostWatts);
        Assert.Null(store.Read(1));
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoost(5), Assert.Single(device.Commands));
        Assert.Contains("175 W", result.Reason);
        Assert.DoesNotContain("总功率上限已确认 155 W", result.Reason);
    }

    [Fact]
    public async Task TgpEditWhileOff_KeepsFieldValueAndDoesNotReplayBoostOrClockDrafts()
    {
        var device = new Device();
        var store = new Store();
        var controller = Create(device, store);
        Assert.True((await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default)).Succeeded);
        Assert.True((await controller.ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 140, default)).Succeeded);
        Assert.Equal(145, device.Ceiling);
        Assert.Equal(140, device.Tgp);
        Assert.True((await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 1, default)).Succeeded);
        Assert.Equal(165, device.Ceiling);
        Assert.Equal(140, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.Equal(new[] { GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false),
            GcuCustomPerformanceCommand.GpuConfigurableTgp(140),
            GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(true) }, device.Commands);
    }

    [Fact]
    public async Task FirmwareMinimum_RestoresOnlyTgpWhenRequestedLowValueIsClamped()
    {
        var device = new Device { MinimumAcceptedTgp = 95 };
        var store = new Store();
        var result = await Create(device, store).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);
        Assert.False(result.Succeeded);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.True(device.BoostEnabled);
        Assert.Null(store.Read(1));
        Assert.Equal(new[] { GcuCustomPerformanceCommand.GpuConfigurableTgp(70),
            GcuCustomPerformanceCommand.GpuConfigurableTgp(150) }, device.Commands);
        Assert.Contains("已恢复", result.Reason);
    }

    [Fact]
    public async Task FailedBudget_RestoresOriginalDisabledTgpSwitchAsWellAsValues()
    {
        var device = new Device { TgpEnabled = false, MinimumAcceptedTgp = 95 };
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);

        Assert.False(result.Succeeded);
        Assert.False(device.TgpEnabled);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
        Assert.True(device.BoostEnabled);
        Assert.Contains("已恢复", result.Reason);
    }

    [Fact]
    public async Task FailedBudget_DoesNotClaimRestoredWhenOriginalTgpSwitchIsNotConfirmed()
    {
        var device = new Device { TgpEnabled = false, IgnoreTgpDisable = true, MinimumAcceptedTgp = 95 };
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("已恢复操作前", result.Reason);
    }

    [Fact]
    public async Task ExternalChanges_InvalidateOldCompensationAndDoNotReplayItsDraft()
    {
        var device = new Device { Tgp = 140 };
        var store = new Store();
        store.Write(1, new(150, 25, 145, 0, false));
        var controller = Create(device, store);
        Assert.Null(await controller.ReadRecordAsync(device.Payload, 1, default));
        var result = await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(145, device.Ceiling);
        Assert.Equal(140, device.Tgp);
        Assert.Null(await controller.ReadRecordAsync(device.Payload, 1, default));
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false), Assert.Single(device.Commands));
    }

    [Fact]
    public async Task ProfileChange_StopsWritesAndDoesNotRestoreIntoForeignProfile()
    {
        var device = new Device { ChangeProfileAfterWrite = true };
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0, default);
        Assert.False(result.Succeeded);
        Assert.Single(device.Commands);
    }

    [Fact]
    public async Task DriverUnavailable_DoesNotPreventOemFieldConfirmationOrPersistATotalBudget()
    {
        var device = new Device();
        var store = new Store();
        var controller = new GpuPowerBudgetController(device, _ => ValueTask.FromResult<PlatformModeSelection?>(device.Mode),
            _ => ValueTask.FromResult<double?>(null), store, (_, _) => Task.CompletedTask);
        var result = await controller.ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 140, default);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(GcuCustomPerformanceCommand.GpuConfigurableTgp(140), Assert.Single(device.Commands));
        Assert.Equal(140, device.Tgp);
        Assert.Contains("不可读", result.Reason);
        Assert.Null(store.Read(1));
    }

    [Fact]
    public async Task Snapshot_ReadsOnePayloadAndShowsRawFieldsInsteadOfLegacyCompensatedTargets()
    {
        var device = new Device { Tgp = 125, BoostWatts = 0, BoostEnabled = false };
        var store = new Store();
        store.Write(1, new(150, 25, 125, 0, false));
        var controller = new GcuCustomPerformanceController(device, Path.GetTempPath(),
            powerBudget: Create(device, store));
        var snapshot = await controller.ReadSnapshotAsync(1, default);
        Assert.NotNull(snapshot.State);
        Assert.Equal(125, snapshot.State.GpuConfigurableTgpWatts);
        Assert.Equal(0, snapshot.State.GpuDynamicBoostWatts);
        Assert.False(snapshot.State.GpuDynamicBoostEnabled);
        Assert.Equal(string.Empty, snapshot.PowerBudgetNote);
        Assert.Equal(95, snapshot.Gpu!.PowerLimits!.TgpMinimum);
        Assert.Equal(1, device.Reads);
        Assert.Empty(device.Commands);
    }

    [Fact]
    public async Task WholeGpuApply_UsesRawFieldsAndDoesNotCompensateConfirmedOff()
    {
        var device = new Device();
        var store = new Store();
        var controller = new GcuCustomPerformanceController(device, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, powerBudget: Create(device, store));
        var result = await controller.ApplyGpuAsync(new(true, 150, false, 25, 0, 0), default);
        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(155, device.Ceiling);
        Assert.Equal(150, device.Tgp);
        Assert.Equal(25, device.BoostWatts);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(175)]
    public async Task TgpFieldConfirmation_DriverLowerOrUnchangedNeverRestoresHigherOldTgp(int ceiling)
    {
        var device = new Device();
        var controller = new GpuPowerBudgetController(device,
            _ => ValueTask.FromResult<PlatformModeSelection?>(device.Mode),
            _ => ValueTask.FromResult<double?>(ceiling), new Store(), (_, _) => Task.CompletedTask);

        var result = await controller.ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 140, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(140, device.Tgp);
        Assert.Equal(GcuCustomPerformanceCommand.GpuConfigurableTgp(140), Assert.Single(device.Commands));
        Assert.Contains($"{ceiling} W", result.Reason);
        Assert.DoesNotContain("总功率上限已确认", result.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    public async Task BoostTierOutsideLiveRange_RejectsWithoutWrites(int watts)
    {
        var device = new Device();
        var result = await Create(device, new Store()).ApplyAsync(
            CustomPerformanceSetting.GpuDynamicBoostWatts, watts, default);

        Assert.False(result.Succeeded);
        Assert.Empty(device.Commands);
    }

    [Fact]
    public async Task BoostTierWhileOff_RejectsWithoutChangingSavedTierOrSwitch()
    {
        var device = new Device { BoostEnabled = false };
        var result = await Create(device, new Store()).ApplyAsync(
            CustomPerformanceSetting.GpuDynamicBoostWatts, 5, default);

        Assert.False(result.Succeeded);
        Assert.False(device.BoostEnabled);
        Assert.Equal(25, device.BoostWatts);
        Assert.Empty(device.Commands);
    }

    [Fact]
    public async Task MissingTgpRange_RejectsTgpWriteWithoutUsingGenericLimits()
    {
        var device = new Device { OmitTgpMinimum = true };
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 140, default);

        Assert.False(result.Succeeded);
        Assert.Empty(device.Commands);
    }

    [Theory]
    [InlineData(90, 15, 95)]
    [InlineData(115, 25, 120)]
    public async Task DifferentHardwareLimits_RejectsTgpAndBoostAboveItsOwnAdvertisedMaximum(
        int tgpMaximum, int boostMaximum, int requestedTgp)
    {
        var device = new Device { Tgp = 80, AdvertisedTgpMinimum = 45,
            AdvertisedTgpMaximum = tgpMaximum, AdvertisedBoostMaximum = boostMaximum, BoostWatts = 10 };
        var controller = Create(device, new Store());

        var tgp = await controller.ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, requestedTgp, default);
        var boost = await controller.ApplyAsync(CustomPerformanceSetting.GpuDynamicBoostWatts, boostMaximum + 5, default);

        Assert.False(tgp.Succeeded);
        Assert.False(boost.Succeeded);
        Assert.Empty(device.Commands);
        Assert.Equal(80, device.Tgp);
        Assert.Equal(10, device.BoostWatts);
    }

    [Fact]
    public async Task AdvertisedMinimumBelowSeventy_AllowsItsLegalLowerValueWithoutRaisingTheFloor()
    {
        var device = new Device { Tgp = 80, AdvertisedTgpMinimum = 45, MinimumAcceptedTgp = 45,
            AdvertisedTgpMaximum = 90, AdvertisedBoostMaximum = 15, BoostWatts = 10 };
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 55, default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(55, device.Tgp);
        Assert.Equal(10, device.BoostWatts);
        Assert.Equal(GcuCustomPerformanceCommand.GpuConfigurableTgp(55), Assert.Single(device.Commands));
    }

    [Fact]
    public async Task WholeBudgetFailure_RestoresOnlyItsChangedTgpAndKeepsOriginalBoostTier()
    {
        var device = new Device { IgnoreBoostSwitch = true };
        var result = await Create(device, new Store()).ApplyBudgetAsync(new(true, 130, false, 25, 0, 0), default);

        Assert.False(result.Succeeded);
        Assert.Equal(150, device.Tgp);
        Assert.True(device.BoostEnabled);
        Assert.Equal(25, device.BoostWatts);
        Assert.Equal(new[] { GcuCustomPerformanceCommand.GpuConfigurableTgp(130),
            GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false),
            GcuCustomPerformanceCommand.GpuConfigurableTgp(150) }, device.Commands);
    }

    [Fact]
    public async Task WholeBudgetFailure_ExternalLaterTgpChangeIsNotOverwrittenDuringRestoration()
    {
        var device = new Device { IgnoreBoostSwitch = true, ExternalTgpAfterRejectedSwitch = 120 };
        var result = await Create(device, new Store()).ApplyBudgetAsync(new(true, 130, false, 25, 0, 0), default);

        Assert.False(result.Succeeded);
        Assert.Equal(120, device.Tgp);
        Assert.DoesNotContain(GcuCustomPerformanceCommand.GpuConfigurableTgp(150), device.Commands);
        Assert.DoesNotContain("已恢复操作前", result.Reason);
    }

    [Fact]
    public async Task FailedTgp_ExternalLaterTargetAlsoPreservesItsDependentTgpEnableSwitch()
    {
        var device = new Device { TgpEnabled = false, MinimumAcceptedTgp = 95,
            ExternalTgpAfterRejectedTgp = 120 };
        var result = await Create(device, new Store()).ApplyAsync(CustomPerformanceSetting.GpuTgpWatts, 70, default);

        Assert.False(result.Succeeded);
        Assert.Equal(120, device.Tgp);
        Assert.True(device.TgpEnabled);
        Assert.Equal(new[] { GcuCustomPerformanceCommand.GpuConfigurableTgpSwitch(true),
            GcuCustomPerformanceCommand.GpuConfigurableTgp(70) }, device.Commands);
        Assert.DoesNotContain("已恢复操作前", result.Reason);
    }

    [Fact]
    public async Task WholeBudgetOff_PreservesExistingTierEvenWhenDisabledDraftContainsZero()
    {
        var device = new Device();
        var result = await Create(device, new Store()).ApplyBudgetAsync(new(true, 150, false, 0, 0, 0), default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.False(device.BoostEnabled);
        Assert.Equal(25, device.BoostWatts);
        Assert.Equal(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false), Assert.Single(device.Commands));
    }

    [Fact]
    public async Task WholeGpuApply_OffDraftZeroDoesNotFailBecauseSavedOnTierRemainsTwentyFive()
    {
        var device = new Device();
        var controller = new GcuCustomPerformanceController(device, Path.GetTempPath(),
            (_, _) => Task.CompletedTask, powerBudget: Create(device, new Store()));
        var result = await controller.ApplyGpuAsync(new(true, 150, false, 0, 0, 0), default);

        Assert.True(result.Succeeded, result.Reason);
        Assert.False(device.BoostEnabled);
        Assert.Equal(25, device.BoostWatts);
        Assert.DoesNotContain(GcuCustomPerformanceCommand.GpuDynamicBoost(0), device.Commands);
    }

    [Fact]
    public void Store_PersistsSeparateProfilesAndDoesNotApplyAnythingOnLoad()
    {
        var path = Path.Combine(Path.GetTempPath(), "revo-budget-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new JsonGpuPowerBudgetStore(path);
            store.Write(1, new(150, 25, 145, 0, false));
            store.Write(2, new(130, 10, 115, 10, true));
            var reopened = new JsonGpuPowerBudgetStore(path);
            Assert.Equal(store.Read(1), reopened.Read(1));
            Assert.Equal(store.Read(2), reopened.Read(2));
            Assert.Null(reopened.Read(3));
        }
        finally { File.Delete(path); }
    }

    private static GpuPowerBudgetController Create(Device device, Store store) =>
        new(device, _ => ValueTask.FromResult<PlatformModeSelection?>(device.Mode),
            _ => ValueTask.FromResult<double?>(device.Ceiling), store, (_, _) => Task.CompletedTask);

    private sealed class Store : IGpuPowerBudgetStore
    {
        private readonly Dictionary<int, GpuPowerBudgetRecord> values = [];
        public GpuPowerBudgetRecord? Read(int profile) => values.GetValueOrDefault(profile);
        public void Write(int profile, GpuPowerBudgetRecord record) => values[profile] = record;
    }

    private sealed class Device : IGcuCustomPerformanceChannel
    {
        public int Tgp { get; set; } = 150;
        public int BoostWatts { get; set; } = 25;
        public bool BoostEnabled { get; set; } = true;
        public bool TgpEnabled { get; set; } = true;
        public bool IgnoreTgpDisable { get; init; }
        public bool IgnoreBoostSwitch { get; init; }
        public bool OmitTgpMinimum { get; init; }
        public int? ExternalTgpAfterRejectedSwitch { get; init; }
        public int? ExternalTgpAfterRejectedTgp { get; init; }
        private int readsAfterRejectedSwitch;
        private int readsAfterRejectedTgp;
        public int MinimumAcceptedTgp { get; init; } = 25;
        public int AdvertisedTgpMinimum { get; init; } = 95;
        public int AdvertisedTgpMaximum { get; init; } = 150;
        public int AdvertisedBoostMaximum { get; init; } = 25;
        public double Ceiling => Tgp + (BoostEnabled ? 25 : 5);
        public PlatformModeSelection Mode { get; set; } = new(PlatformMode.Custom, 1);
        public bool ChangeProfileAfterWrite { get; init; }
        public List<string> Commands { get; } = [];
        public int Reads { get; private set; }
        public string Payload => $$"""
            {"OperatingMode":"3","OverClockingSwitch":"1",
             "TjMax":"105","CPU_TccOffsetSwitch":"1","CPU_TccOffset":"10",
             "CPU_PL4":"145","CPU_PerformanceAndOverClockMenuSwitch":"1",
             "GPU_ConfigurableTGPSwitch":"{{(TgpEnabled ? 1 : 0)}}","GPU_ConfigurableTGPTarget":"{{Tgp}}",
             "GPU_ConfigurableTGPMinimum":"{{AdvertisedTgpMinimum}}","GPU_ConfigurableTGPMaximum":"{{AdvertisedTgpMaximum}}",
             "GPU_DynamicBoostSwitch":"{{(BoostEnabled ? 1 : 0)}}","GPU_DynamicBoost":"{{BoostWatts}}",
             "GPU_DynamicBoostMinimum":"5","GPU_DynamicBoostMaximum":"{{AdvertisedBoostMaximum}}",
             "GPU_CoreClockOffsetOC":"0","GPU_MemoryClockOffsetOC":"0",
             "GPU_CoreClockOffsetMinimumHWOC":"-300","GPU_CoreClockOffsetMaximumHWOC":"250",
             "GPU_MemoryClockOffsetMinimumHWOC":"-1800","GPU_MemoryClockOffsetMaximumHWOC":"1800"}
            """;
        public ValueTask<string> ReadCustomPerformanceStatusAsync(CancellationToken token)
        {
            Reads++;
            if (IgnoreBoostSwitch && Commands.Contains(GcuCustomPerformanceCommand.GpuDynamicBoostSwitch(false)) &&
                ++readsAfterRejectedSwitch == 4 && ExternalTgpAfterRejectedSwitch is { } externalTgp)
                Tgp = externalTgp;
            if (Commands.Contains(GcuCustomPerformanceCommand.GpuConfigurableTgp(70)) &&
                ++readsAfterRejectedTgp == 4 && ExternalTgpAfterRejectedTgp is { } laterTgp)
                Tgp = laterTgp;
            var payload = JsonNode.Parse(Payload)!.AsObject();
            if (OmitTgpMinimum) payload.Remove("GPU_ConfigurableTGPMinimum");
            return ValueTask.FromResult(payload.ToJsonString());
        }
        public ValueTask WriteCustomPerformanceAsync(string payload, CancellationToken token)
        {
            Commands.Add(payload);
            var command = JsonNode.Parse(payload)!;
            if (command["GpuDynamicBoostSwitch"] is { } enabled && !IgnoreBoostSwitch)
                BoostEnabled = enabled.ToString() == "1";
            if (command["GpuDynamicBoost"] is { } watts) BoostWatts = int.Parse(watts.ToString());
            if (command["GpuConfigurableTGPTarget"] is { } tgp) Tgp = Math.Max(MinimumAcceptedTgp, int.Parse(tgp.ToString()));
            if (command["GpuConfigurableTGPSwitch"] is { } tgpSwitch && (tgpSwitch.ToString() == "1" || !IgnoreTgpDisable))
                TgpEnabled = tgpSwitch.ToString() == "1";
            if (ChangeProfileAfterWrite) Mode = new(PlatformMode.Custom, 2);
            return ValueTask.CompletedTask;
        }
    }
}
