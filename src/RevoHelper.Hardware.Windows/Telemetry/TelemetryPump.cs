using RevoHelper.Core.Telemetry;

namespace RevoHelper.Hardware.Windows.Telemetry;

public sealed class TelemetryPump(
    IHardwareTelemetrySource source,
    TimeSpan interval)
{
    public async Task RunAsync(
        Func<HardwareTelemetrySnapshot, ValueTask> onSnapshot,
        Func<Exception, ValueTask> onError,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
                await onSnapshot(snapshot).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                await onError(exception).ConfigureAwait(false);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
