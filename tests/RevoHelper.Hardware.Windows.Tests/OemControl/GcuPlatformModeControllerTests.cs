using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuPlatformModeControllerTests
{
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
    public async Task SetPlatformModeAsync_CustomBaselineWithoutIndexRejectsBeforeAnyWrite()
    {
        var channel = new FakeChannel(GcuCpuPolicyStatusParser.Parse("""{"OperatingMode":3}""")!);

        var result = await new GcuPlatformModeController(channel).SetPlatformModeAsync(new(PlatformMode.Office), CancellationToken.None);

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

        public ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Status);

        public ValueTask WriteModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken)
        {
            writeCount++;
            Writes.Add($"{ModeAction(selection.Mode)}:{selection.ProfileIndex}");
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
