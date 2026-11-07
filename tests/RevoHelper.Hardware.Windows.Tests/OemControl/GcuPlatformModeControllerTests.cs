using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuPlatformModeControllerTests
{
    [Theory]
    [InlineData(0, 0, PlatformMode.TurboQuiet)]
    [InlineData(1, 0, PlatformMode.TurboHigh)]
    public async Task SetBasicTurboModeAsync_DoesNotRequireOrChangeReportedSubStrategy(
        int extreme, int silent, PlatformMode observedMode)
    {
        var channel = new FakeChannel(Status(1) with { Extreme = extreme, Silent = silent })
        {
            IgnoreStrategyWrite = true
        };
        IPlatformModeController controller = new GcuPlatformModeController(channel);

        var result = await controller.SetBasicTurboModeAsync(0, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new(observedMode), result.ConfirmedPlatform);
        Assert.Equal(2, channel.Status.OperatingMode);
        Assert.Equal(["OPERATING_TURBO_MODE:0", "COMPANION:TurboQuiet"], channel.Writes);
        Assert.DoesNotContain("静音", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlatformMode.TurboQuiet, 0)]
    [InlineData(PlatformMode.TurboQuiet, 1)]
    [InlineData(PlatformMode.TurboHigh, 0)]
    public async Task SetPlatformModeAsync_RejectedStrategyPreservesConfirmedBasicTurbo(
        PlatformMode requested, int extreme)
    {
        var channel = new FakeChannel(Status(1) with { Extreme = extreme, Silent = 0 })
        {
            IgnoreStrategyWrite = true
        };

        var result = await new GcuPlatformModeController(channel)
            .SetPlatformModeAsync(new(requested), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(new(extreme == 1 ? PlatformMode.TurboHigh : PlatformMode.TurboQuiet), result.ConfirmedPlatform);
        Assert.Equal(2, channel.Status.OperatingMode);
        Assert.Contains("已保留", result.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain(channel.Writes, write => write.StartsWith("OPERATING_GAMING", StringComparison.Ordinal));
        Assert.Equal(3, channel.Writes.Count);
    }

    [Fact]
    public async Task Diagnostics_ReportUnconvergedObservedModeAndRollbackWithoutExtraReads()
    {
        var entries = new List<(string Area, string Message)>();
        var channel = new FakeChannel(Status(0)) { IgnoreFirstWrite = true };
        var controller = new GcuPlatformModeController(channel, (area, message) => entries.Add((area, message)), TimeSpan.Zero);

        var result = await controller.SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(new PlatformRestorePoint(new(PlatformMode.Office), false), result.RestoredTo);
        Assert.Equal(10, channel.ReadCount); // Baseline + 8 target checks + one rollback check.
        Assert.All(entries, entry => Assert.Equal("platform-gcu", entry.Area));
        Assert.Contains(entries, entry => entry.Message.Contains("stage=mode-verify result=unconfirmed", StringComparison.Ordinal)
            && entry.Message.Contains("attempts=8", StringComparison.Ordinal)
            && entry.Message.Contains("OperatingMode=0", StringComparison.Ordinal)
            && entry.Message.Contains("GamingProfile=0", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Message.Contains("stage=rollback result=success", StringComparison.Ordinal));
        Assert.Equal(["OPERATING_GAMING_MODE:0", "OPERATING_OFFICE_MODE:0", "COMPANION:Office"], channel.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Diagnostics_ThrowingSinkDoesNotChangeGcuCommandsOrResult(bool ignoreFirstWrite)
    {
        var baselineChannel = new FakeChannel(Status(0)) { IgnoreFirstWrite = ignoreFirstWrite };
        var tracedChannel = new FakeChannel(Status(0)) { IgnoreFirstWrite = ignoreFirstWrite };
        var baseline = await new GcuPlatformModeController(baselineChannel)
            .SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);
        var traced = await new GcuPlatformModeController(tracedChannel,
            (_, _) => throw new InvalidOperationException("simulated diagnostic sink failure"))
            .SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);

        Assert.Equal(baseline, traced);
        Assert.Equal(baselineChannel.ReadCount, tracedChannel.ReadCount);
        Assert.Equal(baselineChannel.Writes, tracedChannel.Writes);
        Assert.Equal(baselineChannel.Status, tracedChannel.Status);
    }

    [Theory]
    [InlineData(PlatformMode.Office)]
    [InlineData(PlatformMode.Balanced)]
    [InlineData(PlatformMode.TurboQuiet)]
    public async Task SetPlatformModeAsync_DefaultStockProfileAcceptsModeOnlyReadback(PlatformMode target)
    {
        var channel = new FakeChannel(GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":0}""")!)
        {
            OmitProfileIndexes = true
        };

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(target), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.DoesNotContain(channel.Writes, write => write.StartsWith("SET_CPU_CORE_OFFSET", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetPlatformModeAsync_CustomBaselineWithoutIndexAllowsRequestedStockMode()
    {
        var channel = new FakeChannel(GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":3}""")!);

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["OPERATING_OFFICE_MODE:0", "COMPANION:Office"], channel.Writes);
    }

    [Theory]
    [InlineData(PlatformMode.Office, null)]
    [InlineData(PlatformMode.Balanced, null)]
    [InlineData(PlatformMode.TurboQuiet, null)]
    [InlineData(PlatformMode.Office, 99)]
    [InlineData(PlatformMode.Balanced, 99)]
    [InlineData(PlatformMode.TurboQuiet, 99)]
    public async Task SetPlatformModeAsync_UnknownInitialModeAttemptsOnlyRequestedBasicMode(
        PlatformMode target, int? initialMode)
    {
        var channel = new FakeChannel(Status(0) with
        {
            OperatingMode = initialMode, Extreme = null, Silent = null
        });

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(target), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(2, channel.Writes.Count);
        Assert.Equal("COMPANION:" + target, channel.Writes[1]);
        Assert.Equal(target, GcuPlatformModeStatusMapper.Map(channel.Status)!.Mode);
    }

    [Fact]
    public async Task SetPlatformModeAsync_UnknownInitialModeAndUnconfirmedTargetDoesNotInventRestore()
    {
        var channel = new FakeChannel(Status(0) with { OperatingMode = null }) { IgnoreFirstWrite = true };

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain("恢复", result.Reason, StringComparison.Ordinal);
        Assert.Null(channel.Status.OperatingMode);
        Assert.Equal(["OPERATING_GAMING_MODE:0"], channel.Writes);
    }

    [Fact]
    public async Task SetPlatformModeAsync_UnavailableInitialStatusDoesNotWrite()
    {
        var channel = new FakeChannel(Status(0)) { FailInitialStatusRead = true };

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("channel unavailable", result.Reason, StringComparison.Ordinal);
        Assert.Empty(channel.Writes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SetPlatformModeAsync_InvalidCustomSlotStillRejectsBeforeAnyWrite(int slot)
    {
        var channel = new FakeChannel(Status(0) with { OperatingMode = null });

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Custom, slot), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task SetPlatformModeAsync_EnteringCustomDoesNotVerifyAnOldInactiveSlot()
    {
        var channel = new FakeChannel(Status(0) with { CustomProfileIndex = 0 })
        {
            AccumulateModeOnlyReadback = true
        };

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Custom, 1), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, channel.Status.OperatingMode);
        Assert.Null(channel.Status.CustomProfileIndex);
    }

    [Theory]
    [InlineData(PlatformMode.Balanced, 1)]
    [InlineData(PlatformMode.Custom, 1)]
    public async Task SetPlatformModeAsync_MissingIndexDoesNotVerifyNonDefaultOrCustomProfile(PlatformMode target, int profileIndex)
    {
        var channel = new FakeChannel(Status(0)) { OmitProfileIndexes = true };

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(target, profileIndex), CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SetPlatformModeAsync_KnownStockIndexMismatchStillFails()
    {
        var channel = new FakeChannel(Status(0)) { MismatchFirstProfileIndex = true };

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(0, channel.Status.OperatingMode);
    }

    [Fact]
    public async Task SetPlatformModeAsync_CustomProfileStillRequiresExactZeroBasedReadback()
    {
        var channel = new FakeChannel(Status(0));

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Custom, 2), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, channel.Status.CustomProfileIndex);
    }

    [Fact]
    public async Task SetPlatformModeAsync_BasicTurboDoesNotWriteASubStrategy()
    {
        var channel = new FakeChannel(Status(0) with { Extreme = null, Silent = null });
        var controller = new GcuPlatformModeController(channel);

        var result = await controller.SetPlatformModeAsync(new(PlatformMode.TurboQuiet), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(2, channel.Status.OperatingMode);
        Assert.DoesNotContain("静音", result.Reason, StringComparison.Ordinal);
        Assert.Equal(["OPERATING_TURBO_MODE:0", "COMPANION:TurboQuiet"], channel.Writes);
    }

    [Fact]
    public async Task SetPlatformModeAsync_UnverifiedHighStrategyRejectsBeforeWriting()
    {
        var channel = new FakeChannel(Status(0) with { Extreme = null, Silent = null });
        var controller = new GcuPlatformModeController(channel);

        var result = await controller.SetPlatformModeAsync(new(PlatformMode.TurboHigh), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task SetPlatformModeAsync_BasicTurboRollbackDoesNotWriteASubStrategy()
    {
        var channel = new FakeChannel(Status(2) with { Extreme = null, Silent = null })
        {
            IgnoreFirstWrite = true
        };
        var controller = new GcuPlatformModeController(channel);

        var result = await controller.SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(2, channel.Status.OperatingMode);
        Assert.Equal(["OPERATING_GAMING_MODE:0", "OPERATING_TURBO_MODE:0", "COMPANION:TurboQuiet"], channel.Writes);
    }

    [Fact]
    public async Task SetPlatformModeAsync_ReportsSuccessOnlyAfterMatchingReadback()
    {
        var channel = new FakeChannel(Status(0));
        var controller = new GcuPlatformModeController(channel);

        var result = await controller.SetPlatformModeAsync(
            new PlatformModeSelection(PlatformMode.Balanced), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(1, channel.Status.OperatingMode);
        Assert.Equal(["OPERATING_GAMING_MODE:0", "COMPANION:Balanced"], channel.Writes);
    }

    [Fact]
    public async Task SetPlatformModeAsync_WhenModeDoesNotConverge_RestoresPriorMode()
    {
        var channel = new FakeChannel(Status(0)) { IgnoreFirstWrite = true };
        var controller = new GcuPlatformModeController(channel);

        var result = await controller.SetPlatformModeAsync(
            new PlatformModeSelection(PlatformMode.Balanced), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal(0, channel.Status.OperatingMode);
        Assert.Equal(
            ["OPERATING_GAMING_MODE:0", "OPERATING_OFFICE_MODE:0", "COMPANION:Office"],
            channel.Writes);
    }

    [Theory]
    [InlineData(PlatformMode.TurboQuiet, "SET_CPU_CORE_OFFSET_SILENT")]
    [InlineData(PlatformMode.TurboHigh, "SET_CPU_CORE_OFFSET_EXTREME")]
    public async Task SetPlatformModeAsync_TurboAppliesAndVerifiesSubStrategy(
        PlatformMode mode,
        string expectedStrategyAction)
    {
        var channel = new FakeChannel(Status(0));
        var controller = new GcuPlatformModeController(channel);

        var result = await controller.SetPlatformModeAsync(new(mode), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(
            ["OPERATING_TURBO_MODE:0", "COMPANION:" + mode, expectedStrategyAction],
            channel.Writes);
    }

    [Fact]
    public async Task Rollback_ToBasicTurboDoesNotSwitchQuietStrategyOn()
    {
        var channel = new FakeChannel(Status(2) with { Extreme = 0, Silent = 0 }) { IgnoreFirstWrite = true };

        var result = await new GcuPlatformModeController(channel, lateConvergenceWindow: TimeSpan.Zero)
            .SetPlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(["OPERATING_OFFICE_MODE:0", "OPERATING_TURBO_MODE:0", "COMPANION:TurboQuiet"], channel.Writes);
        Assert.Equal(0, channel.Status.Silent);
        Assert.Equal(0, channel.Status.Extreme);
        Assert.Equal(new PlatformRestorePoint(new(PlatformMode.TurboQuiet), false), result.RestoredTo);
    }

    [Theory]
    [InlineData(0, 1, "SET_CPU_CORE_OFFSET_SILENT")]
    [InlineData(1, 0, "SET_CPU_CORE_OFFSET_EXTREME")]
    public async Task Rollback_RestoresStrategyThatWasActuallyOn(int extreme, int silent, string expected)
    {
        var channel = new FakeChannel(Status(2) with { Extreme = extreme, Silent = silent }) { IgnoreFirstWrite = true };

        var result = await new GcuPlatformModeController(channel, lateConvergenceWindow: TimeSpan.Zero)
            .SetPlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains(expected, channel.Writes);
        Assert.True(result.RestoredTo?.RestoreStrategy);
    }

    [Theory]
    [InlineData(0, 0, false, null)]
    [InlineData(0, 1, true, "SET_CPU_CORE_OFFSET_SILENT")]
    [InlineData(1, 0, true, "SET_CPU_CORE_OFFSET_EXTREME")]
    public async Task RestorePoint_WritesStrategyOnlyWhenItWasOn(int extreme, int silent, bool restoreStrategy, string? expected)
    {
        var channel = new FakeChannel(Status(2) with { Extreme = extreme, Silent = silent });
        IPlatformModeController controller = new GcuPlatformModeController(channel);

        var point = await controller.CaptureRestorePointAsync(CancellationToken.None);
        var result = await controller.RestorePlatformModeAsync(point!, CancellationToken.None);

        Assert.Equal(restoreStrategy, point!.RestoreStrategy);
        Assert.True(result.Succeeded, result.Reason);
        if (expected is null)
            Assert.DoesNotContain(channel.Writes, write => write.StartsWith("SET_CPU_CORE_OFFSET", StringComparison.Ordinal));
        else
            Assert.Contains(expected, channel.Writes);
    }

    [Fact]
    public async Task LateTransition_IsConfirmedByReadbackWithoutSendingRollback()
    {
        var entries = new List<string>();
        var channel = new FakeChannel(Status(1)) { ApplyFirstWriteAfterReads = 10 };

        var result = await new GcuPlatformModeController(channel, (_, message) => entries.Add(message))
            .SetPlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(0, channel.Status.OperatingMode);
        Assert.Equal(["OPERATING_OFFICE_MODE:0", "COMPANION:Office"], channel.Writes);
        Assert.Contains(entries, entry => entry.Contains("stage=mode-verify result=unconfirmed", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.Contains("stage=mode-verify-late result=success", StringComparison.Ordinal)
            && entry.Contains("waitedMs=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LateWindowExpiry_RecordsItAndStillRestoresPriorMode()
    {
        var entries = new List<string>();
        var channel = new FakeChannel(Status(1)) { IgnoreFirstWrite = true };

        var result = await new GcuPlatformModeController(channel, (_, message) => entries.Add(message), TimeSpan.FromMilliseconds(600))
            .SetPlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal(1, channel.Status.OperatingMode);
        Assert.Contains(entries, entry => entry.Contains("stage=mode-verify-late result=unconfirmed", StringComparison.Ordinal));
    }

    private static GcuCpuPolicyStatus Status(int mode) =>
        new(45, 45, 5, 210, 5, 210, mode, 0, 0, 0, 1, 0, mode == 2 ? 1 : 0);

    private sealed class FakeChannel(GcuCpuPolicyStatus status) : IGcuPlatformModeChannel
    {
        private int writeCount;
        public GcuCpuPolicyStatus Status { get; private set; } = status;
        public List<string> Writes { get; } = [];
        public bool IgnoreFirstWrite { get; init; }
        public bool OmitProfileIndexes { get; init; }
        public bool MismatchFirstProfileIndex { get; init; }
        public bool AccumulateModeOnlyReadback { get; init; }
        public bool FailInitialStatusRead { get; init; }
        public bool IgnoreStrategyWrite { get; init; }
        public int ApplyFirstWriteAfterReads { get; init; }
        public int ReadCount { get; private set; }
        private PlatformModeSelection? delayedWrite;
        private int readsUntilDelayedWrite;

        public ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            if (delayedWrite is { } delayed && --readsUntilDelayedWrite <= 0)
            {
                delayedWrite = null;
                Status = Status with { OperatingMode = delayed.Mode == PlatformMode.Office ? 0 : 1 };
            }
            return FailInitialStatusRead
                ? ValueTask.FromException<GcuCpuPolicyStatus>(new InvalidOperationException("channel unavailable"))
                : ValueTask.FromResult(Status);
        }

        public ValueTask WriteModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken)
        {
            writeCount++;
            Writes.Add($"{ModeAction(selection.Mode)}:{selection.ProfileIndex}");
            if (ApplyFirstWriteAfterReads > 0 && writeCount == 1)
            {
                delayedWrite = selection;
                readsUntilDelayedWrite = ApplyFirstWriteAfterReads;
                return ValueTask.CompletedTask;
            }
            if (!IgnoreFirstWrite || writeCount > 1)
            {
                var previous = Status;
                Status = selection.Mode switch
                {
                    PlatformMode.Office => Status with { OperatingMode = 0, OfficeProfileIndex = selection.ProfileIndex },
                    PlatformMode.Balanced => Status with { OperatingMode = 1, GamingProfileIndex = selection.ProfileIndex },
                    PlatformMode.TurboQuiet or PlatformMode.TurboHigh => Status with { OperatingMode = 2, TurboProfileIndex = selection.ProfileIndex },
                    _ => Status with { OperatingMode = 3, CustomProfileIndex = selection.ProfileIndex - 1 }
                };
                if (MismatchFirstProfileIndex && writeCount == 1)
                    Status = Status with { GamingProfileIndex = selection.ProfileIndex + 1 };
                if (OmitProfileIndexes)
                    Status = Status with { OfficeProfileIndex = null, GamingProfileIndex = null,
                        TurboProfileIndex = null, CustomProfileIndex = null };
                if (AccumulateModeOnlyReadback)
                    Status = GcuCpuPolicyStatusAccumulator.Merge(previous,
                        GcuCpuPolicyStatusParser.Parse($"{{\"OperatingMode\":{Status.OperatingMode}}}")!);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteTurboStrategyAsync(PlatformMode mode, CancellationToken cancellationToken)
        {
            Writes.Add(Action(mode));
            if (IgnoreStrategyWrite) return ValueTask.CompletedTask;
            Status = mode == PlatformMode.TurboHigh
                ? Status with { Extreme = 1, Silent = 0 }
                : Status with { Extreme = 0, Silent = 1 };
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteCompanionModeAsync(PlatformMode mode, CancellationToken cancellationToken)
        {
            Writes.Add("COMPANION:" + mode);
            return ValueTask.CompletedTask;
        }

        private static string Action(PlatformMode mode) => mode switch
        {
            PlatformMode.Office => "OPERATING_OFFICE_MODE",
            PlatformMode.Balanced => "OPERATING_GAMING_MODE",
            PlatformMode.TurboQuiet => "SET_CPU_CORE_OFFSET_SILENT",
            PlatformMode.TurboHigh => "SET_CPU_CORE_OFFSET_EXTREME",
            PlatformMode.Custom => "OPERATING_CUSTOM_MODE",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        private static string ModeAction(PlatformMode mode) => mode switch
        {
            PlatformMode.Office => "OPERATING_OFFICE_MODE",
            PlatformMode.Balanced => "OPERATING_GAMING_MODE",
            PlatformMode.TurboQuiet or PlatformMode.TurboHigh => "OPERATING_TURBO_MODE",
            PlatformMode.Custom => "OPERATING_CUSTOM_MODE",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }
}
