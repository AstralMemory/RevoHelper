using RevoHelper.Core.Telemetry;

namespace RevoHelper.Hardware.Windows.Telemetry;

public interface IHardwareTelemetrySource : IAsyncDisposable
{
    ValueTask<HardwareTelemetrySnapshot> ReadAsync(CancellationToken cancellationToken);
}
