using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuCpuPowerLimitController(IGcuCpuPolicyChannel channel)
    : ICpuPolicyController, ICpuPolicyRestorePointProvider
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    // Reading a field, including a mismatched confirmation, never establishes ownership.
    private readonly Dictionary<int, OwnedValues> ownedByProfile = [];

    public async ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
    {
        var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        return HasValidPowerSnapshot(status) ? Policy(status) : null;
    }

    public async ValueTask<CpuPolicyRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var baseline = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            return HasValidPowerSnapshot(baseline) && Profile(baseline) is not null
                ? new(Policy(baseline), token => RestoreSnapshotAsync(baseline, token)) : null;
        }
        finally { operationLock.Release(); }
    }

    public async ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        GcuCpuPolicyStatus? baseline = null;
        OperationState? operation = null;
        try
        {
            if (policy.MaxFrequencyMhz is not null)
                return ControlResult.Rejected("当前测试版尚未开放 CPU 频率上限写入。");
            baseline = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!HasValidPowerSnapshot(baseline) || Profile(baseline) is null)
                return ControlResult.Rejected("未取得完整 CPU 功耗回读或当前自定义方案身份，未写入。");
            if (Validate(policy, baseline) is { } invalid) return ControlResult.Rejected(invalid);

            operation = new(baseline);
            await ApplyPairAsync(operation, policy.Pl1Watts, policy.Pl2Watts, cancellationToken).ConfigureAwait(false);
            await ReadExpectedAsync(operation, cancellationToken).ConfigureAwait(false);
            return new(true, $"已应用并回读：PL1 {policy.Pl1Watts} W / PL2 {policy.Pl2Watts} W。");
        }
        catch (Exception exception)
        {
            var restoration = "本次未发送功耗写入。";
            if (baseline is not null && operation?.AttemptedWrite == true)
            {
                using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var restored = await RestoreCoreAsync(baseline, recovery.Token).ConfigureAwait(false);
                    restoration = restored.Succeeded ? "已恢复原值。" : restored.Reason;
                }
                catch (Exception recoveryFailure) { restoration = $"恢复原值未确认：{recoveryFailure.Message}"; }
            }
            if (exception is OperationCanceledException) throw;
            return ControlResult.Rejected($"功耗设置失败：{exception.Message}；{restoration}");
        }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> RestoreSnapshotAsync(GcuCpuPolicyStatus baseline, CancellationToken token)
    {
        await operationLock.WaitAsync(token).ConfigureAwait(false);
        try { return await RestoreCoreAsync(baseline, token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return ControlResult.Rejected($"CPU 功耗恢复未确认：{exception.Message}"); }
        finally { operationLock.Release(); }
    }

    private async ValueTask<ControlResult> RestoreCoreAsync(GcuCpuPolicyStatus baseline, CancellationToken token)
    {
        var current = await ReadForProfileAsync(baseline, token).ConfigureAwait(false);
        var owned = ownedByProfile.GetValueOrDefault(Profile(baseline)!.Value);
        if ((current.Pl1Watts != baseline.Pl1Watts && current.Pl1Watts != owned?.Pl1) ||
            (current.Pl2Watts != baseline.Pl2Watts && current.Pl2Watts != owned?.Pl2))
            return ControlResult.Rejected("检测到外部 CPU 功耗修改，已保留当前值；未确认恢复原值。");

        var operation = new OperationState(current);
        await ApplyPairAsync(operation, baseline.Pl1Watts, baseline.Pl2Watts, token).ConfigureAwait(false);
        await ReadExpectedAsync(operation, token).ConfigureAwait(false);
        return new(true, "CPU 功耗原值已恢复并回读确认。");
    }

    private async Task ApplyPairAsync(OperationState operation, int targetPl1, int targetPl2, CancellationToken token)
    {
        if (targetPl1 > operation.Expected.Pl2Watts)
        {
            await WriteAndVerifyAsync(operation, false, targetPl2, token).ConfigureAwait(false);
            await WriteAndVerifyAsync(operation, true, targetPl1, token).ConfigureAwait(false);
        }
        else
        {
            await WriteAndVerifyAsync(operation, true, targetPl1, token).ConfigureAwait(false);
            await WriteAndVerifyAsync(operation, false, targetPl2, token).ConfigureAwait(false);
        }
    }

    private async Task WriteAndVerifyAsync(OperationState operation, bool pl1, int watts, CancellationToken token)
    {
        await ReadExpectedAsync(operation, token).ConfigureAwait(false);
        if ((pl1 ? operation.Expected.Pl1Watts : operation.Expected.Pl2Watts) == watts) return;
        token.ThrowIfCancellationRequested();
        operation.Expected = pl1 ? operation.Expected with { Pl1Watts = watts }
            : operation.Expected with { Pl2Watts = watts };
        operation.AttemptedWrite = true;
        var profile = Profile(operation.Expected)!.Value;
        var owned = ownedByProfile.GetValueOrDefault(profile) ?? new(null, null);
        ownedByProfile[profile] = pl1 ? owned with { Pl1 = watts } : owned with { Pl2 = watts };
        if (pl1) await channel.WritePl1Async(watts, token).ConfigureAwait(false);
        else await channel.WritePl2Async(watts, token).ConfigureAwait(false);
        await ReadExpectedAsync(operation, token).ConfigureAwait(false);
    }

    private async Task ReadExpectedAsync(OperationState operation, CancellationToken token)
    {
        var current = await ReadForProfileAsync(operation.Expected, token).ConfigureAwait(false);
        if (current.Pl1Watts != operation.Expected.Pl1Watts || current.Pl2Watts != operation.Expected.Pl2Watts)
            throw new InvalidOperationException($"CPU 功耗值已变化或回读未收敛：PL1={current.Pl1Watts} W，PL2={current.Pl2Watts} W。");
    }

    private async Task<GcuCpuPolicyStatus> ReadForProfileAsync(GcuCpuPolicyStatus expected, CancellationToken token)
    {
        var current = await channel.ReadStatusAsync(token).ConfigureAwait(false);
        if (Profile(current) is not { } profile || profile != Profile(expected))
            throw new InvalidOperationException("当前自定义方案已切换或无法确认，已停止写入及跨方案恢复。");
        if (!HasValidPowerSnapshot(current))
            throw new InvalidOperationException("当前 CPU 功耗回读不完整或无效，已停止写入及恢复。");
        return current;
    }

    private static int? Profile(GcuCpuPolicyStatus status) =>
        status.OperatingMode == 3 && status.CustomProfileIndex is >= 0 and <= 4 ? status.CustomProfileIndex : null;

    private static bool HasValidPowerSnapshot(GcuCpuPolicyStatus status) =>
        status.HasCpuPowerLimitSnapshot && status.Pl1MinimumWatts >= 0 && status.Pl2MinimumWatts >= 0 &&
        status.Pl1Watts > 0 && status.Pl2Watts > 0 &&
        status.Pl1Watts >= status.Pl1MinimumWatts && status.Pl1Watts <= status.Pl1MaximumWatts &&
        status.Pl2Watts >= status.Pl2MinimumWatts && status.Pl2Watts <= status.Pl2MaximumWatts &&
        status.Pl1Watts <= status.Pl2Watts;

    private static CpuPolicy Policy(GcuCpuPolicyStatus status) => new(status.Pl1Watts, status.Pl2Watts, null);

    private static string? Validate(CpuPolicy policy, GcuCpuPolicyStatus status)
    {
        if (policy.Pl1Watts > policy.Pl2Watts) return "PL1 不能高于 PL2。";
        if (policy.Pl1Watts < status.Pl1MinimumWatts || policy.Pl1Watts > status.Pl1MaximumWatts)
            return $"PL1 必须位于本机 GCU 报告的 {status.Pl1MinimumWatts}–{status.Pl1MaximumWatts} W 范围内。";
        if (policy.Pl2Watts < status.Pl2MinimumWatts || policy.Pl2Watts > status.Pl2MaximumWatts)
            return $"PL2 必须位于本机 GCU 报告的 {status.Pl2MinimumWatts}–{status.Pl2MaximumWatts} W 范围内。";
        return null;
    }

    private sealed record OwnedValues(int? Pl1, int? Pl2);
    private sealed class OperationState(GcuCpuPolicyStatus initial)
    {
        public GcuCpuPolicyStatus Expected { get; set; } = initial;
        public bool AttemptedWrite { get; set; }
    }
}
