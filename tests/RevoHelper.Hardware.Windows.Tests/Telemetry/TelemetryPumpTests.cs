using RevoHelper.Core.Telemetry;
using RevoHelper.Hardware.Windows.Telemetry;

namespace RevoHelper.Hardware.Windows.Tests.Telemetry;

public sealed class TelemetryPumpTests
{
    [Fact]
    public async Task RunAsync_PublishesSequentialSnapshotsUntilCancelled()
    {
        var source = new RecordingSource();
        var pump = new TelemetryPump(source, TimeSpan.FromMilliseconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var published = 0;

        await pump.RunAsync(
            snapshot =>
            {
                published++;
                if (published == 2)
                {
                    cancellation.Cancel();
                }

                return ValueTask.CompletedTask;
            },
            _ => ValueTask.CompletedTask,
            cancellation.Token);

        Assert.Equal(2, published);
        Assert.Equal(1, source.MaximumConcurrentReads);
    }

    [Fact]
    public async Task RunAsync_ReportsReadFailureAndContinuesPolling()
    {
        var source = new RecordingSource { FailFirstRead = true };
        var pump = new TelemetryPump(source, TimeSpan.FromMilliseconds(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var errors = 0;
        var snapshots = 0;

        await pump.RunAsync(
            _ =>
            {
                snapshots++;
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            },
            _ =>
            {
                errors++;
                return ValueTask.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(1, errors);
        Assert.Equal(1, snapshots);
        Assert.Equal(2, source.ReadCount);
    }

    private sealed class RecordingSource : IHardwareTelemetrySource
    {
        private int concurrentReads;

        public bool FailFirstRead { get; init; }

        public int ReadCount { get; private set; }

        public int MaximumConcurrentReads { get; private set; }

        public async ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            var active = Interlocked.Increment(ref concurrentReads);
            MaximumConcurrentReads = Math.Max(MaximumConcurrentReads, active);
            try
            {
                await Task.Yield();
                if (FailFirstRead && ReadCount == 1)
                {
                    throw new IOException("Simulated sensor failure");
                }

                return new(DateTimeOffset.UtcNow, []);
            }
            finally
            {
                Interlocked.Decrement(ref concurrentReads);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
