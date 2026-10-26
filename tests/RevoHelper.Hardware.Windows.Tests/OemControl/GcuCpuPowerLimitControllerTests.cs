using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCpuPowerLimitControllerTests
{
    [Fact]
    public async Task ReadCpuPolicyAsync_ReturnsLiveGcuValues()
    {
        var channel = new FakeChannel(Status(45, 65));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ReadCpuPolicyAsync(CancellationToken.None);

        Assert.Equal(new CpuPolicy(45, 65, null), result);
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_IncreasingAboveOldPl2_WritesPl2BeforePl1()
    {
        var channel = new FakeChannel(Status(45, 65));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(80, 90, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["PL2=90", "PL1=80"], channel.Writes);
        Assert.Equal((80, 90), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_LoweringPl2BelowOldPl1_WritesPl1BeforePl2()
    {
        var channel = new FakeChannel(Status(80, 90));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(35, 45, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["PL1=35", "PL2=45"], channel.Writes);
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_RejectsValuesOutsideLiveBounds()
    {
        var channel = new FakeChannel(Status(45, 65) with { Pl1MinimumWatts = 20, Pl1MaximumWatts = 100, Pl2MinimumWatts = 25, Pl2MaximumWatts = 110 });
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(10, 65, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
        Assert.Contains("20", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_FailedConvergence_RestoresPriorPair()
    {
        var channel = new FakeChannel(Status(45, 65))
        {
            IgnoreWriteNumber = 2
        };
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(80, 90, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("已恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal((45, 65), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
    }

    [Fact]
    public async Task Apply_ProfileChangesBeforeFailedReadback_DoesNotRestoreIntoForeignProfile()
    {
        var channel = new FakeChannel(Status(45, 65));
        channel.AfterWrite = number => { if (number == 1) channel.SetStatus(Status(80, 100) with { CustomProfileIndex = 1 }); };
        var result = await new GcuCpuPowerLimitController(channel).ApplyCpuPolicyAsync(new(35, 55, null), default);

        Assert.False(result.Succeeded);
        Assert.Equal((80, 100), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
        Assert.Equal(["PL1=35"], channel.Writes);
        Assert.Contains("方案", result.Reason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Apply_ExternalPairChangeDuringConfirmation_PreservesItIncludingOuterRestorePoint(bool sameField)
    {
        var channel = new FakeChannel(Status(45, 65));
        channel.AfterWrite = number =>
        {
            if (number == 1) channel.SetStatus(sameField
                ? channel.Status with { Pl1Watts = 40 }
                : channel.Status with { Pl2Watts = 90 });
        };
        var controller = new GcuCpuPowerLimitController(channel);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);
        var result = await controller.ApplyCpuPolicyAsync(new(35, 55, null), default);
        var afterExternal = channel.Status;
        var restored = await baseline!.RestoreAsync(default);

        Assert.False(result.Succeeded);
        Assert.False(restored.Succeeded);
        Assert.Equal(afterExternal, channel.Status);
        Assert.Equal(["PL1=35"], channel.Writes);
    }

    [Fact]
    public async Task Apply_UnknownProfileIdentity_DoesNotWrite()
    {
        var channel = new FakeChannel(Status(45, 65) with { CustomProfileIndex = null });
        var result = await new GcuCpuPowerLimitController(channel).ApplyCpuPolicyAsync(new(35, 55, null), default);
        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task Apply_CanceledAfterFirstWrite_RestoresWithIndependentToken()
    {
        using var cancellation = new CancellationTokenSource();
        var channel = new FakeChannel(Status(45, 65));
        channel.AfterWrite = number => { if (number == 1) cancellation.Cancel(); };
        var controller = new GcuCpuPowerLimitController(channel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ApplyCpuPolicyAsync(new(35, 55, null), cancellation.Token).AsTask());
        Assert.Equal((45, 65), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
        Assert.Equal(["PL1=35", "PL1=45"], channel.Writes);
    }

    private static GcuCpuPolicyStatus Status(int pl1, int pl2) =>
        new(pl1, pl2, 5, 210, 5, 210, OperatingMode: 3, CustomProfileIndex: 0);

    [Fact]
    public async Task IncompleteSnapshot_CannotBeReadCapturedOrWritten()
    {
        var channel = new FakeChannel(Status(45, 65) with { HasCpuPowerLimitSnapshot = false });
        var controller = new GcuCpuPowerLimitController(channel);
        Assert.Null(await controller.ReadCpuPolicyAsync(default));
        Assert.Null(await controller.CaptureRestorePointAsync(default));
        Assert.False((await controller.ApplyCpuPolicyAsync(new(35, 55, null), default)).Succeeded);
        Assert.Empty(channel.Writes);
    }

    [Fact]
    public async Task RestorePoint_ReturnsOwnedFieldsToBaselineAndRejectsAnotherProfile()
    {
        var channel = new FakeChannel(Status(45, 65));
        var controller = new GcuCpuPowerLimitController(channel);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(80, 90, null), default)).Succeeded);
        Assert.True((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal((45, 65), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));

        channel.SetStatus(Status(80, 100) with { CustomProfileIndex = 1 });
        var count = channel.Writes.Count;
        Assert.False((await baseline.RestoreAsync(default)).Succeeded);
        Assert.Equal(count, channel.Writes.Count);
        Assert.Equal((80, 100), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
    }

    private sealed class FakeChannel(GcuCpuPolicyStatus status) : IGcuCpuPolicyChannel
    {
        private int writeNumber;
        public GcuCpuPolicyStatus Status { get; private set; } = status;
        public List<string> Writes { get; } = [];
        public int? IgnoreWriteNumber { get; init; }
        public Action<int>? AfterWrite { get; set; }
        public void SetStatus(GcuCpuPolicyStatus value) => Status = value;

        public ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Status);
        }

        public ValueTask WritePl1Async(int watts, CancellationToken cancellationToken)
        {
            Writes.Add($"PL1={watts}");
            writeNumber++;
            if (writeNumber != IgnoreWriteNumber) Status = Status with { Pl1Watts = watts };
            AfterWrite?.Invoke(writeNumber);
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePl2Async(int watts, CancellationToken cancellationToken)
        {
            Writes.Add($"PL2={watts}");
            writeNumber++;
            if (writeNumber != IgnoreWriteNumber) Status = Status with { Pl2Watts = watts };
            AfterWrite?.Invoke(writeNumber);
            return ValueTask.CompletedTask;
        }
    }
}
