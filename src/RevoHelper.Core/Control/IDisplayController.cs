namespace RevoHelper.Core.Control;

public sealed record DisplaySnapshot(
    string DeviceName,
    string DisplayName,
    int Width,
    int Height,
    int CurrentRefreshRateHz,
    IReadOnlyList<int> AvailableRefreshRatesHz,
    int DesktopRefreshRateHz = 0)
{
    public bool RequiresRefreshRateSynchronization =>
        DesktopRefreshRateHz > 0 && DesktopRefreshRateHz != CurrentRefreshRateHz;
}

public interface IDisplayController
{
    ValueTask<DisplaySnapshot> ReadAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> SetRefreshRateAsync(int refreshRateHz, CancellationToken cancellationToken);
    ValueTask<ControlResult> SetTemporaryRefreshRateAsync(int refreshRateHz, CancellationToken cancellationToken);
    ValueTask<ControlResult> RestoreTemporaryRefreshRateAsync(CancellationToken cancellationToken);
}
