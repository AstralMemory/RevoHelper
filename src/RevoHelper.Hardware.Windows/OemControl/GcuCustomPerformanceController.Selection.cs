using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed partial class GcuCustomPerformanceController
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private PlatformModeSelection? operationSelection;

    private bool HasSelectionReader => readMode is not null || powerBudget is not null;

    private ValueTask<PlatformModeSelection?> ReadSelectionAsync(CancellationToken token) =>
        readMode is not null ? readMode(token)
            : powerBudget is not null ? powerBudget.ReadSelectionAsync(token)
            : ValueTask.FromResult<PlatformModeSelection?>(null);

    private async ValueTask<ControlResult> ExecuteForSelectionAsync(int? profileIndex, bool requireCustom,
        Func<ValueTask<ControlResult>> operation, CancellationToken token)
    {
        if (profileIndex is { } profile) ValidateProfile(profile);
        await operationGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            operationSelection = HasSelectionReader ? await ReadSelectionAsync(token).ConfigureAwait(false) : null;
            if (HasSelectionReader && (operationSelection is null ||
                (requireCustom && operationSelection.Mode != PlatformMode.Custom) ||
                (profileIndex is { } expected && operationSelection != new PlatformModeSelection(PlatformMode.Custom, expected))))
                return ControlResult.Rejected("当前自定义方案已变化或未确认；未写入草稿。");
            var result = await operation().ConfigureAwait(false);
            await EnsureOperationSelectionAsync(token).ConfigureAwait(false);
            return result;
        }
        catch (SelectionChangedException)
        {
            return ControlResult.Rejected("性能档位或自定义方案已切换，已停止本次参数写入；请重新读取状态。");
        }
        finally
        {
            operationSelection = null;
            operationGate.Release();
        }
    }

    private async ValueTask<bool> IsRequestedProfileActiveAsync(int profileIndex, CancellationToken token) =>
        !HasSelectionReader || await ReadSelectionAsync(token).ConfigureAwait(false) == new PlatformModeSelection(PlatformMode.Custom, profileIndex);

    private async ValueTask EnsureOperationSelectionAsync(CancellationToken token)
    {
        if (operationSelection is { } expected && await ReadSelectionAsync(token).ConfigureAwait(false) != expected)
            throw new SelectionChangedException();
    }

    private async ValueTask<string> ReadOperationPayloadAsync(CancellationToken token)
    {
        await EnsureOperationSelectionAsync(token).ConfigureAwait(false);
        var payload = await channel.ReadCustomPerformanceStatusAsync(token).ConfigureAwait(false);
        await EnsureOperationSelectionAsync(token).ConfigureAwait(false);
        return payload;
    }

    private async ValueTask WriteOperationAsync(string command, CancellationToken token)
    {
        await EnsureOperationSelectionAsync(token).ConfigureAwait(false);
        await channel.WriteCustomPerformanceAsync(command, token).ConfigureAwait(false);
        await EnsureOperationSelectionAsync(token).ConfigureAwait(false);
    }

    private sealed class SelectionChangedException : IOException { }
}
