using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelRaplCpuPolicyControllerTests
{
    [Fact]
    public async Task Apply_WritesPackageLimitAndVerifiesTheActualMsr()
    {
        var msr = new FakeMsrAccess(3, 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47));
        var controller = new IntelRaplCpuPolicyController(msr);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(35, 45, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new CpuPolicy(35, 45, null), await controller.ReadCpuPolicyAsync(CancellationToken.None));
        Assert.Single(msr.Writes);
        Assert.Equal(IntelRaplCpuPolicyController.PackagePowerLimitMsr, msr.Writes[0].Register);
    }

    [Fact]
    public async Task Apply_WhenReadbackDoesNotConverge_RestoresOriginalRegister()
    {
        var original = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47);
        var msr = new FakeMsrAccess(3, original) { IgnoreFirstWrite = true };
        var controller = new IntelRaplCpuPolicyController(msr);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(35, 45, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal(original, msr.PackagePowerLimit);
        Assert.Single(msr.Writes); // The original fields are already intact; no restore write is needed.
    }

    [Fact]
    public async Task Read_WhenBackendUnavailable_ReturnsNull()
    {
        var controller = new IntelRaplCpuPolicyController(new FakeMsrAccess(3, 0) { IsAvailable = false });

        Assert.Null(await controller.ReadCpuPolicyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Read_WhenFirmwareLockedPackagePowerLimit_ReturnsNullSoBackendIsNotExposed()
    {
        var locked = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47) | (1UL << 63);
        var controller = new IntelRaplCpuPolicyController(new FakeMsrAccess(3, locked));

        Assert.Null(await controller.ReadCpuPolicyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task VerifyWriteAccess_WritesTheExactOriginalRegisterAndReadsItBack()
    {
        var original = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47);
        var msr = new FakeMsrAccess(3, original);
        var controller = new IntelRaplCpuPolicyController(msr);

        var result = await controller.VerifyWriteAccessAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Single(msr.Writes);
        Assert.Equal(original, msr.Writes[0].Value);
        Assert.Equal(original, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePoint_RestoresDisabledEnableBitsAndFractionalPowerEncoding()
    {
        var original = 361UL | (523UL << 32);
        var msr = new FakeMsrAccess(3, original);
        var controller = new IntelRaplCpuPolicyController(msr);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);

        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), default)).Succeeded);
        Assert.True((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal(original, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePoint_KeepsCurrentUncontrolledBits()
    {
        var original = 360UL | (520UL << 32);
        var msr = new FakeMsrAccess(3, original);
        var controller = new IntelRaplCpuPolicyController(msr);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), default)).Succeeded);
        msr.SetExternal(msr.PackagePowerLimit | (1UL << 17));

        Assert.True((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal(original | (1UL << 17), msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePoint_ExternalPowerChangeIsPreserved()
    {
        var msr = new FakeMsrAccess(3, 360UL | (520UL << 32));
        var controller = new IntelRaplCpuPolicyController(msr);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), default)).Succeeded);
        var external = IntelRaplPowerLimitCodec.Encode(3, msr.PackagePowerLimit, 40, 50);
        msr.SetExternal(external);

        Assert.False((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal(external, msr.PackagePowerLimit);
        Assert.Single(msr.Writes);
    }

    private const ulong Unit = 3;

    private static ulong Raw(int pl1, int pl2) => IntelRaplPowerLimitCodec.Encode(Unit, 0, pl1, pl2);

    [Fact]
    public async Task ExitRestore_AfterPresetWritesTheModeSwitchesLeftAlone_RestoresTheValueFromBeforeTheFirstWrite()
    {
        // As on the 275HX: an OEM mode change keeps MSR 0x610, so Balanced starts from Office's write.
        var boot = Raw(210, 210);
        var msr = new FakeMsrAccess(Unit, boot);
        var platform = new FakePlatformController(new(PlatformMode.Office));
        var controller = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));

        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);
        platform.Current = new(PlatformMode.Balanced);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(85, 90, null), default)).Succeeded);
        platform.Current = new(PlatformMode.TurboHigh);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(150, 160, null), default)).Succeeded);

        var restore = await controller.RestoreDirectOverrideAsync(default);

        Assert.True(restore.Succeeded, restore.Reason);
        Assert.Equal(boot, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task ExitRestore_WhenFirmwareReprogramsTheRegisterOnAModeSwitch_RestoresThatModesValue()
    {
        var msr = new FakeMsrAccess(Unit, Raw(54, 65));
        var platform = new FakePlatformController(new(PlatformMode.Balanced));
        var controller = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        platform.Current = new(PlatformMode.TurboHigh);
        var turbo = Raw(80, 90);
        msr.SetExternal(turbo);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(70, 80, null), default)).Succeeded);

        Assert.True((await controller.RestoreDirectOverrideAsync(default)).Succeeded);
        Assert.Equal(turbo, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task ScheduleFailureRollingBackToAnOwnedWrite_ExitStillRestoresTheValueFromBeforeTheFirstWrite()
    {
        var boot = Raw(210, 210);
        var msr = new FakeMsrAccess(Unit, boot);
        var platform = new FakePlatformController(new(PlatformMode.Balanced));
        var cpu = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));
        var frequency = new FakeFrequencyController();
        var schedule = new PerformanceScheduleController(platform, cpu, new FakeBoostController(), frequency);
        var boost = new ProcessorBoostPolicy(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled);

        var office = await schedule.ApplyAsync(new(new(PlatformMode.Office), new(45, 45, null), boost,
            ProcessorFrequencyLimitPolicy.Uniform(0)), platformAlreadySelected: false, default);
        Assert.True(office.Succeeded, office.Reason);
        frequency.Fail = true;
        var balanced = await schedule.ApplyAsync(new(new(PlatformMode.Balanced), new(85, 90, null), boost,
            ProcessorFrequencyLimitPolicy.Uniform(3100)), platformAlreadySelected: false, default);
        Assert.False(balanced.Succeeded);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Office), platform.Current);
        Assert.Equal(Raw(45, 45), msr.PackagePowerLimit);

        var restore = await cpu.RestoreDirectOverrideAsync(default);

        Assert.True(restore.Succeeded, restore.Reason);
        Assert.Equal(boot, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task ExitRestoreInCustom_UndoesThePresetOverrideTheRegisterStillHolds()
    {
        var boot = Raw(210, 210);
        var msr = new FakeMsrAccess(Unit, boot);
        var platform = new FakePlatformController(new(PlatformMode.Office));
        var controller = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        platform.Current = new(PlatformMode.Custom, 1);
        var restore = await controller.RestoreDirectOverrideAsync(default);

        Assert.True(restore.Succeeded, restore.Reason);
        Assert.Equal(boot, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task ExitRestoreInCustom_KeepsLimitsTheOemProfileWrote()
    {
        var msr = new FakeMsrAccess(Unit, Raw(210, 210));
        var platform = new FakePlatformController(new(PlatformMode.Office));
        var controller = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        platform.Current = new(PlatformMode.Custom, 1);
        var oem = Raw(120, 140);
        msr.SetExternal(oem);
        var writes = msr.Writes.Count;
        var restore = await controller.RestoreDirectOverrideAsync(default);

        Assert.False(restore.Succeeded);
        Assert.Equal(oem, msr.PackagePowerLimit);
        Assert.Equal(writes, msr.Writes.Count);
    }

    [Fact]
    public async Task ExplicitWriteFailure_KeepsTheLastConfirmedOwnership()
    {
        var boot = Raw(210, 210);
        var msr = new FakeMsrAccess(Unit, boot);
        var controller = new IntelRaplCpuPolicyController(msr);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        msr.RejectWrites = true;
        Assert.False((await controller.ApplyCpuPolicyAsync(new(85, 90, null), default)).Succeeded);
        Assert.Equal(Raw(45, 45), msr.PackagePowerLimit);
        msr.RejectWrites = false;

        Assert.True((await controller.RestoreSessionRootAsync(default)).Succeeded);
        Assert.Equal(boot, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task PartlyAppliedWrite_IsNotRestoredOverAndDoesNotBecomeTheRoot()
    {
        var boot = Raw(210, 210);
        var msr = new FakeMsrAccess(Unit, boot);
        var controller = new IntelRaplCpuPolicyController(msr);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        // Only PL1 of the next write lands, so the register holds neither value.
        var mixed = (Raw(45, 45) & ~0x7FFFUL) | (85UL * 8);
        msr.LandsAs = _ =>
        {
            msr.LandsAs = null;
            return mixed;
        };
        Assert.False((await controller.ApplyCpuPolicyAsync(new(85, 90, null), default)).Succeeded);
        Assert.Equal(mixed, msr.PackagePowerLimit);
        var writes = msr.Writes.Count;

        Assert.False((await controller.RestoreSessionRootAsync(default)).Succeeded);
        Assert.Equal(mixed, msr.PackagePowerLimit);
        Assert.Equal(writes, msr.Writes.Count);

        Assert.True((await controller.ApplyCpuPolicyAsync(new(60, 70, null), default)).Succeeded);
        Assert.True((await controller.RestoreSessionRootAsync(default)).Succeeded);
        Assert.Equal(boot, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePointFindingItsValueAlreadyBack_KeepsTheOwnershipItFound()
    {
        // Firmware that programs each mode on a change: Balanced 70, Office the same raw 45 this session wrote.
        var msr = new FakeMsrAccess(Unit, Raw(210, 210));
        var platform = new FakePlatformController(new(PlatformMode.Office));
        var cpu = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));
        Assert.True((await cpu.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);
        platform.OnChange = selection =>
            msr.SetExternal(selection.Mode == PlatformMode.Balanced ? Raw(70, 70) : Raw(45, 45));
        var schedule = new PerformanceScheduleController(
            platform, cpu, new FakeBoostController(), new FakeFrequencyController { Fail = true });

        var balanced = await schedule.ApplyAsync(new(new(PlatformMode.Balanced), new(85, 90, null),
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), ProcessorFrequencyLimitPolicy.Uniform(3100)),
            platformAlreadySelected: false, default);
        Assert.False(balanced.Succeeded);
        Assert.Equal(new PlatformModeSelection(PlatformMode.Office), platform.Current);
        Assert.Equal(Raw(45, 45), msr.PackagePowerLimit);
        var writes = msr.Writes.Count;

        // The 45 now on the register is the firmware's: the exit leaves it.
        Assert.True((await cpu.RestoreDirectOverrideAsync(default)).Succeeded);
        Assert.Equal(Raw(45, 45), msr.PackagePowerLimit);
        Assert.Equal(writes, msr.Writes.Count);
    }

    [Fact]
    public async Task RestorePoint_DoesNotTakeOverAWriteThatStartedFromALaterValue()
    {
        var msr = new FakeMsrAccess(Unit, Raw(60, 60));
        var platform = new FakePlatformController(new(PlatformMode.Balanced));
        var cpu = new PlatformAwareCpuPolicyController(
            platform, new FakeOemCpuController(), new IntelRaplCpuPolicyController(msr));
        // Firmware reprograms the register after the schedule captured it, while Boost is applied.
        var later = Raw(80, 90);
        var boost = new FakeBoostController();
        boost.OnApply = () =>
        {
            boost.OnApply = null;
            msr.SetExternal(later);
        };
        var schedule = new PerformanceScheduleController(
            platform, cpu, boost, new FakeFrequencyController { Fail = true });

        var result = await schedule.ApplyAsync(new(new(PlatformMode.Balanced), new(45, 45, null),
            new(ProcessorBoostMode.Enabled, ProcessorBoostMode.Enabled), ProcessorFrequencyLimitPolicy.Uniform(3100)),
            platformAlreadySelected: true, default);

        Assert.False(result.Succeeded);
        Assert.Equal(Raw(45, 45), msr.PackagePowerLimit);
        Assert.True((await cpu.RestoreDirectOverrideAsync(default)).Succeeded);
        Assert.Equal(later, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RejectedWriteThatCannotBeReadBack_KeepsTheLastConfirmedOwnership()
    {
        var boot = Raw(210, 210);
        var msr = new FakeMsrAccess(Unit, boot);
        var controller = new IntelRaplCpuPolicyController(msr);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        msr.RejectWrites = true;
        msr.FailReadsAfterNextWrite = 1;
        Assert.False((await controller.ApplyCpuPolicyAsync(new(85, 90, null), default)).Succeeded);
        msr.RejectWrites = false;

        Assert.True((await controller.RestoreSessionRootAsync(default)).Succeeded);
        Assert.Equal(boot, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task WriteThatFindsAnotherWritersValue_EndsTheOwnership()
    {
        var msr = new FakeMsrAccess(Unit, Raw(210, 210));
        var controller = new IntelRaplCpuPolicyController(msr);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(45, 45, null), default)).Succeeded);

        // Firmware sets its own limits while the write is under way.
        var firmware = Raw(65, 70);
        msr.LandsAs = _ =>
        {
            msr.LandsAs = null;
            return firmware;
        };
        Assert.False((await controller.ApplyCpuPolicyAsync(new(85, 90, null), default)).Succeeded);
        Assert.Equal(firmware, msr.PackagePowerLimit);

        Assert.True((await controller.ApplyCpuPolicyAsync(new(100, 110, null), default)).Succeeded);
        Assert.True((await controller.RestoreSessionRootAsync(default)).Succeeded);
        Assert.Equal(firmware, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task ExitRestore_WithoutAnyWrite_TouchesNothing()
    {
        var msr = new FakeMsrAccess(Unit, Raw(210, 210));
        var controller = new IntelRaplCpuPolicyController(msr);

        Assert.True((await controller.RestoreSessionRootAsync(default)).Succeeded);
        Assert.Empty(msr.Writes);
    }

    private sealed class FakePlatformController(PlatformModeSelection current) : IPlatformModeController
    {
        public PlatformModeSelection Current { get; set; } = current;
        public Action<PlatformModeSelection>? OnChange { get; set; }

        public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken token) =>
            ValueTask.FromResult<PlatformModeSelection?>(Current);

        public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken token)
        {
            if (selection != Current) OnChange?.Invoke(selection);
            Current = selection;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeOemCpuController : ICpuPolicyController
    {
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken token) => ValueTask.FromResult<CpuPolicy?>(null);

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken token) =>
            ValueTask.FromResult(ControlResult.Rejected("not custom"));
    }

    private sealed class FakeBoostController : IProcessorBoostController
    {
        private ProcessorBoostPolicy current = new(ProcessorBoostMode.Disabled, ProcessorBoostMode.Disabled);

        public Action? OnApply { get; set; }

        public ValueTask<ProcessorBoostPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(current);

        public ValueTask<ControlResult> ApplyAsync(ProcessorBoostPolicy policy, CancellationToken token)
        {
            OnApply?.Invoke();
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeFrequencyController : IProcessorFrequencyLimitController
    {
        private ProcessorFrequencyLimitPolicy current = ProcessorFrequencyLimitPolicy.Uniform(0);

        public bool Fail { get; set; }

        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken token) => ValueTask.FromResult(current);

        public ValueTask<ControlResult> ApplyAsync(ProcessorFrequencyLimitPolicy policy, CancellationToken token)
        {
            if (Fail && policy != current) return ValueTask.FromResult(ControlResult.Rejected("frequency write failed"));
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }

    private sealed class FakeMsrAccess(ulong powerUnit, ulong packagePowerLimit) : IMsrAccess
    {
        private int writeCount;
        private int failingReads;
        public bool IsAvailable { get; init; } = true;
        public int FailReadsAfterNextWrite { get; set; }
        public string AvailabilityReason => IsAvailable ? "available" : "unavailable";
        public bool IgnoreFirstWrite { get; init; }
        public bool RejectWrites { get; set; }
        public Func<ulong, ulong>? LandsAs { get; set; }
        public ulong PackagePowerLimit { get; private set; } = packagePowerLimit;
        public List<(uint Register, ulong Value)> Writes { get; } = [];
        public void SetExternal(ulong value) => PackagePowerLimit = value;

        public bool TryRead(uint register, out ulong value)
        {
            if (register == IntelRaplCpuPolicyController.PackagePowerLimitMsr && failingReads > 0)
            {
                failingReads--;
                value = 0;
                return false;
            }

            value = register switch
            {
                IntelRaplCpuPolicyController.RaplPowerUnitMsr => powerUnit,
                IntelRaplCpuPolicyController.PackagePowerLimitMsr => PackagePowerLimit,
                _ => 0
            };
            return IsAvailable && register is IntelRaplCpuPolicyController.RaplPowerUnitMsr
                or IntelRaplCpuPolicyController.PackagePowerLimitMsr;
        }

        public bool TryWrite(uint register, ulong value)
        {
            if (FailReadsAfterNextWrite > 0)
            {
                failingReads = FailReadsAfterNextWrite;
                FailReadsAfterNextWrite = 0;
            }

            if (!IsAvailable || RejectWrites || register != IntelRaplCpuPolicyController.PackagePowerLimitMsr) return false;
            Writes.Add((register, value));
            writeCount++;
            if (!(IgnoreFirstWrite && writeCount == 1)) PackagePowerLimit = LandsAs?.Invoke(value) ?? value;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
