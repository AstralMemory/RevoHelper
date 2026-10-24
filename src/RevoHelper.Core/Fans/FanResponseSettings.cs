using RevoHelper.Core.Control;

namespace RevoHelper.Core.Fans;

public sealed record FanResponseSettings(
    bool IndependentCpuGpuControl,
    bool TransitionSensitivityEnabled,
    int TransitionSensitivity)
{
    // FanSwitchSpeed is sent as raw milliseconds per 0.5% fan-speed change.
    public const int MinimumTransitionDelayMilliseconds = 0;
    public const int MaximumTransitionDelayMilliseconds = 5000;
    public const int TransitionDelayStepMilliseconds = 100;
}

public sealed record FanRestoreResult(ControlResult Result, FanCurveTable? Table);

public interface IOemFanController
{
    ValueTask<FanCurveTable?> ReadAsync(CancellationToken cancellationToken);
    ValueTask<FanResponseSettings?> ReadResponseAsync(CancellationToken cancellationToken);
    ValueTask<ControlResult> ApplyAsync(
        FanCurveTable table,
        FanResponseSettings response,
        CancellationToken cancellationToken);
    ValueTask<FanRestoreResult> RestoreAsync(string profileName, CancellationToken cancellationToken);
}
