using RevoHelper.Core.Fans;

namespace RevoHelper.Hardware.Windows.OemControl;

public interface IGcuFanControlChannel
{
    ValueTask<FanCurveTable?> ReadFanTableAsync(CancellationToken cancellationToken);
    ValueTask<FanResponseSettings?> ReadFanResponseAsync(CancellationToken cancellationToken);
    ValueTask WriteFanControlAsync(string payload, CancellationToken cancellationToken);
}
