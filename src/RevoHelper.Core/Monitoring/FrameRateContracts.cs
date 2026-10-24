namespace RevoHelper.Core.Monitoring;

/// <summary>Start time is part of the identity: a reused PID must not inherit a pinned target.</summary>
public sealed record ProcessTarget(
    int ProcessId,
    DateTimeOffset StartedAtUtc,
    string ProcessName,
    string DisplayName)
{
    public override string ToString() => $"{DisplayName} ({ProcessId})";
}

public enum FrameRateStatus
{
    Stopped,
    Starting,
    NoTarget,
    WaitingForFrames,
    Available,
    TargetExited,
    Unavailable
}

public sealed record FrameRateSnapshot(
    ProcessTarget? Target,
    double? FramesPerSecond,
    DateTimeOffset ObservedAtUtc,
    FrameRateStatus Status,
    string? Detail = null);

public interface IFrameRateSource : IAsyncDisposable
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    void SetPinnedTarget(ProcessTarget? target);
    FrameRateSnapshot GetSnapshot();
}

public interface IProcessTargetProvider
{
    ProcessTarget? GetForegroundTarget();
    bool IsAlive(ProcessTarget target);
    IReadOnlyList<ProcessTarget> GetWindowTargets();
}
