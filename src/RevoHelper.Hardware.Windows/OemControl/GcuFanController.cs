using RevoHelper.Core.Control;
using RevoHelper.Core.Fans;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuFanController(
    IGcuFanControlChannel channel,
    Func<TimeSpan, CancellationToken, Task>? verificationDelay = null,
    Func<CancellationToken, ValueTask<PlatformModeSelection?>>? readMode = null) : IOemFanController
{
    private readonly SemaphoreSlim operationLock = new(1, 1);
    private readonly Func<TimeSpan, CancellationToken, Task> delay = verificationDelay ?? Task.Delay;
    private PlatformModeSelection? operationSelection;

    private ValueTask<PlatformModeSelection?> ReadSelectionAsync(CancellationToken token) =>
        readMode is not null ? readMode(token) : ValueTask.FromResult<PlatformModeSelection?>(null);

    public ValueTask<FanCurveTable?> ReadAsync(CancellationToken cancellationToken) =>
        channel.ReadFanTableAsync(cancellationToken);

    public ValueTask<FanResponseSettings?> ReadResponseAsync(CancellationToken cancellationToken) =>
        channel.ReadFanResponseAsync(cancellationToken);

    public async ValueTask<ControlResult> ApplyAsync(
        FanCurveTable table,
        FanResponseSettings response,
        CancellationToken cancellationToken)
    {
        table = table with { IndependentCpuGpuControl = response.IndependentCpuGpuControl };
        var validation = FanCurveValidator.Validate(table);
        if (!validation.IsValid)
        {
            return ControlResult.Rejected($"风扇曲线校验失败：{string.Join("；", validation.Errors)}");
        }
        if (string.IsNullOrWhiteSpace(table.Name))
        {
            return ControlResult.Rejected("GCU 未返回当前风扇方案名称，不能安全写入。");
        }
        if (response.TransitionSensitivity is < FanResponseSettings.MinimumTransitionDelayMilliseconds or
            > FanResponseSettings.MaximumTransitionDelayMilliseconds)
        {
            return ControlResult.Rejected("风扇转换延迟必须位于 0–5000 ms/0.5%。");
        }

        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        FanCurveTable? baselineTable = null;
        FanResponseSettings? baselineResponse = null;
        try
        {
            operationSelection = await ReadSelectionAsync(cancellationToken).ConfigureAwait(false);
            if (readMode is not null && operationSelection is not { Mode: PlatformMode.Custom })
                return ControlResult.Rejected("未确认当前自定义方案；未写入风扇设置。");
            baselineTable = await channel.ReadFanTableAsync(cancellationToken).ConfigureAwait(false);
            baselineResponse = await channel.ReadFanResponseAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSelectionAsync(cancellationToken).ConfigureAwait(false);
            if (baselineTable is null || baselineResponse is null)
            {
                return ControlResult.Rejected("未取得完整原厂风扇回读，已阻止写入。");
            }
            if (!baselineTable.Name.Equals(table.Name, StringComparison.OrdinalIgnoreCase))
                return ControlResult.Rejected("当前风扇方案已变化；未写入旧方案草稿。");

            await WriteAllAsync(table, response, cancellationToken).ConfigureAwait(false);
            if (await WaitForConvergenceAsync(table, response, cancellationToken).ConfigureAwait(false))
            {
                return new ControlResult(true, "CPU/GPU 风扇曲线与响应设置已写入并回读确认。");
            }
            throw new InvalidOperationException("GCU 风扇回读未与目标设置收敛。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (exception is SelectionChangedException)
                return ControlResult.Rejected("性能档位或自定义方案已切换，已停止写入；未向其它方案恢复旧曲线。");
            if (baselineTable is null || baselineResponse is null)
            {
                return ControlResult.Rejected($"风扇设置失败：{exception.Message}");
            }
            try
            {
                await WriteAllAsync(baselineTable, baselineResponse, cancellationToken).ConfigureAwait(false);
                if (await WaitForConvergenceAsync(baselineTable, baselineResponse, cancellationToken).ConfigureAwait(false))
                {
                    return ControlResult.Rejected($"风扇设置失败，已恢复原值：{exception.Message}");
                }
                return ControlResult.Rejected($"风扇设置失败且恢复回读不一致：{exception.Message}");
            }
            catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
            {
                return ControlResult.Rejected($"风扇设置失败，恢复原值也失败：{exception.Message}；{rollbackException.Message}");
            }
        }
        finally
        {
            operationSelection = null;
            operationLock.Release();
        }
    }

    public async ValueTask<FanRestoreResult> RestoreAsync(string profileName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return new(ControlResult.Rejected("GCU 未返回当前风扇方案名称，无法恢复原厂曲线。"), null);
        }
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            operationSelection = await ReadSelectionAsync(cancellationToken).ConfigureAwait(false);
            if (readMode is not null && operationSelection is not { Mode: PlatformMode.Custom })
                return new(ControlResult.Rejected("未确认当前自定义方案；未发送恢复命令。"), null);
            var before = await channel.ReadFanTableAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSelectionAsync(cancellationToken).ConfigureAwait(false);
            if (before is null || !before.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                return new(ControlResult.Rejected("当前风扇方案已变化；未恢复旧方案。"), null);
            await WriteAsync(GcuFanControlCommand.Restore(profileName), cancellationToken).ConfigureAwait(false);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await delay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
                var table = await channel.ReadFanTableAsync(cancellationToken).ConfigureAwait(false);
                await EnsureSelectionAsync(cancellationToken).ConfigureAwait(false);
                if (table is not null && table.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase) &&
                    FanCurveValidator.Validate(table).IsValid)
                {
                    // The protocol exposes the current table, not a factory reference
                    // or a reset acknowledgement. A valid table alone cannot prove a reset.
                    return new(new ControlResult(true, "恢复命令已发送，已读取当前曲线；原厂默认值未确认。"), table);
                }
            }
            return new(ControlResult.Rejected("已发送恢复命令，但当前曲线未确认；请重新读取。"), null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(ControlResult.Rejected($"恢复原厂风扇曲线失败：{exception.Message}"), null);
        }
        finally
        {
            operationSelection = null;
            operationLock.Release();
        }
    }

    private async Task WriteAllAsync(
        FanCurveTable table,
        FanResponseSettings response,
        CancellationToken cancellationToken)
    {
        await WriteAsync(GcuFanControlCommand.SetCurve(table, FanChannel.Cpu), cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(GcuFanControlCommand.SetCurve(table, FanChannel.Gpu), cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(
            GcuFanControlCommand.SetIndependentOutput(table.Name, response.IndependentCpuGpuControl), cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(
            GcuFanControlCommand.SetTransitionSensitivityEnabled(response.TransitionSensitivityEnabled), cancellationToken)
            .ConfigureAwait(false);
        await WriteAsync(
            GcuFanControlCommand.SetTransitionSensitivity(response.TransitionSensitivity), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<bool> WaitForConvergenceAsync(
        FanCurveTable targetTable,
        FanResponseSettings targetResponse,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                await delay(TimeSpan.FromMilliseconds(180), cancellationToken).ConfigureAwait(false);
            }
            var table = await channel.ReadFanTableAsync(cancellationToken).ConfigureAwait(false);
            var response = await channel.ReadFanResponseAsync(cancellationToken).ConfigureAwait(false);
            await EnsureSelectionAsync(cancellationToken).ConfigureAwait(false);
            if (TableMatches(table, targetTable) && response == targetResponse) return true;
        }
        return false;
    }

    private async ValueTask WriteAsync(string payload, CancellationToken token)
    {
        await EnsureSelectionAsync(token).ConfigureAwait(false);
        await channel.WriteFanControlAsync(payload, token).ConfigureAwait(false);
        await EnsureSelectionAsync(token).ConfigureAwait(false);
    }

    private async ValueTask EnsureSelectionAsync(CancellationToken token)
    {
        if (readMode is not null && (operationSelection is null ||
            await ReadSelectionAsync(token).ConfigureAwait(false) != operationSelection))
            throw new SelectionChangedException();
    }

    private sealed class SelectionChangedException : IOException { }

    private static bool TableMatches(FanCurveTable? actual, FanCurveTable expected) =>
        actual is not null &&
        actual.Name.Equals(expected.Name, StringComparison.OrdinalIgnoreCase) &&
        actual.IndependentCpuGpuControl == expected.IndependentCpuGpuControl &&
        ChannelMatches(actual.Cpu, expected.Cpu) &&
        ChannelMatches(actual.Gpu, expected.Gpu);

    private static bool ChannelMatches(IReadOnlyList<FanCurvePoint> actual, IReadOnlyList<FanCurvePoint> expected) =>
        actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.DutyPercent == pair.Second.DutyPercent);
}
