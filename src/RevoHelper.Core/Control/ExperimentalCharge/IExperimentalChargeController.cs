namespace RevoHelper.Core.Control.ExperimentalCharge;

public sealed record ExperimentalChargeStatus(
    bool Enabled = false,
    bool Busy = false,
    bool RecoveryPending = false,
    int LimitPercent = 80,
    string Message = "默认关闭；60% / 80% 手动限充，100% 不限制并按原生策略充电。",
    string? SessionId = null,
    bool PowerPaused = false);

public interface IExperimentalChargeController
{
    ExperimentalChargeStatus Status { get; }
    event EventHandler? StatusChanged;
    Task EnableAsync(int limitPercent, CancellationToken cancellationToken = default);
    Task StopAsync(string reason, CancellationToken cancellationToken = default);
    Task RecoverAsync(CancellationToken cancellationToken = default);
    void RequestStop(string reason);
}
