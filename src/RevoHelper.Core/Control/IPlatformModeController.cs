namespace RevoHelper.Core.Control;

/// <summary>
/// A mode observed before a transition. RestoreStrategy is false when no firmware
/// sub-strategy was actually on, so a rollback must not switch one on.
/// </summary>
public sealed record PlatformRestorePoint(PlatformModeSelection Selection, bool RestoreStrategy);

public interface IPlatformModeController
{
    ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken);

    ValueTask<ControlResult> SetBasicTurboModeAsync(int profileIndex, CancellationToken cancellationToken) =>
        SetPlatformModeAsync(new(PlatformMode.TurboQuiet, profileIndex), cancellationToken);

    async ValueTask<PlatformRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken) =>
        await ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false) is { } selection
            ? new(selection, true)
            : null;

    ValueTask<ControlResult> RestorePlatformModeAsync(PlatformRestorePoint point, CancellationToken cancellationToken) =>
        SetPlatformModeAsync(point.Selection, cancellationToken);
}
