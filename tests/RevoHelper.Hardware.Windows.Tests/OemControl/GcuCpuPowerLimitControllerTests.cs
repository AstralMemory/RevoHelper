using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.Tests.OemControl;

public sealed class GcuCpuPowerLimitControllerTests
{
    [Fact]
    public async Task ReadCpuPolicyAsync_ReturnsLiveGcuValues()
    {
        var channel = new FakeChannel(new GcuCpuPolicyStatus(45, 65, 5, 210, 5, 210));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ReadCpuPolicyAsync(CancellationToken.None);

        Assert.Equal(new CpuPolicy(45, 65, null), result);
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_IncreasingAboveOldPl2_WritesPl2BeforePl1()
    {
        var channel = new FakeChannel(new GcuCpuPolicyStatus(45, 65, 5, 210, 5, 210));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(80, 90, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["PL2=90", "PL1=80"], channel.Writes);
        Assert.Equal((80, 90), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_LoweringPl2BelowOldPl1_WritesPl1BeforePl2()
    {
        var channel = new FakeChannel(new GcuCpuPolicyStatus(80, 90, 5, 210, 5, 210));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(35, 45, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(["PL1=35", "PL2=45"], channel.Writes);
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_RejectsValuesOutsideLiveBounds()
    {
        var channel = new FakeChannel(new GcuCpuPolicyStatus(45, 65, 20, 100, 25, 110));
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(10, 65, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(channel.Writes);
        Assert.Contains("20", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyCpuPolicyAsync_FailedConvergence_RestoresPriorPair()
    {
        var channel = new FakeChannel(new GcuCpuPolicyStatus(45, 65, 5, 210, 5, 210))
        {
            IgnoreWriteNumber = 2
        };
        var controller = new GcuCpuPowerLimitController(channel);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(80, 90, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("已恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal((45, 65), (channel.Status.Pl1Watts, channel.Status.Pl2Watts));
    }

    private sealed class FakeChannel(GcuCpuPolicyStatus status) : IGcuCpuPolicyChannel
    {
        private int writeNumber;
        public GcuCpuPolicyStatus Status { get; private set; } = status;
        public List<string> Writes { get; } = [];
        public int? IgnoreWriteNumber { get; init; }

        public ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Status);

        public ValueTask WritePl1Async(int watts, CancellationToken cancellationToken)
        {
            Writes.Add($"PL1={watts}");
            writeNumber++;
            if (writeNumber != IgnoreWriteNumber) Status = Status with { Pl1Watts = watts };
            return ValueTask.CompletedTask;
        }

        public ValueTask WritePl2Async(int watts, CancellationToken cancellationToken)
        {
            Writes.Add($"PL2={watts}");
            writeNumber++;
            if (writeNumber != IgnoreWriteNumber) Status = Status with { Pl2Watts = watts };
            return ValueTask.CompletedTask;
        }
    }
}
