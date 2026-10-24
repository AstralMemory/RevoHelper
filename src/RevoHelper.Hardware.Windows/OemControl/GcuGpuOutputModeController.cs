using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public sealed class GcuGpuOutputModeController(
    IGcuGpuOutputModeChannel channel,
    IGpuOutputModeCapabilitySource capabilitySource) : IGpuOutputModeController
{
    private readonly SemaphoreSlim operationLock = new(1, 1);

    public async ValueTask<GpuOutputModeState?> ReadStateAsync(CancellationToken cancellationToken)
    {
        var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
        return new(status.Mode, Combine(status, capabilitySource.Read()));
    }

    public async ValueTask<ControlResult> SetModeAsync(GpuOutputMode mode, CancellationToken cancellationToken)
    {
        await operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        GpuOutputMode? baseline = null;
        try
        {
            var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            baseline = status.Mode;
            var capabilities = Combine(status, capabilitySource.Read());
            if (!capabilities.Supports(mode))
                return ControlResult.Rejected($"当前机型未确认支持{Label(mode)}，没有写入硬件。");
            if (baseline == mode)
                return new(true, $"当前已经是{Label(mode)}。");

            await ApplyAndVerifyAsync(mode, cancellationToken).ConfigureAwait(false);
            return new(true, $"已写入并回读验证：{Label(mode)}；重启 Windows 后生效。");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (baseline is null)
                return ControlResult.Rejected($"显卡输出模式切换失败：{exception.Message}");
            try
            {
                await ApplyAndVerifyAsync(baseline.Value, cancellationToken).ConfigureAwait(false);
                return ControlResult.Rejected($"显卡输出模式切换失败，已恢复原设置：{exception.Message}");
            }
            catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
            {
                return ControlResult.Rejected($"显卡输出模式切换及恢复均失败：{exception.Message}；{rollbackException.Message}");
            }
        }
        finally
        {
            operationLock.Release();
        }
    }

    private async Task ApplyAndVerifyAsync(GpuOutputMode mode, CancellationToken cancellationToken)
    {
        await channel.WriteModeAsync(mode, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var status = await channel.ReadStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.Mode == mode) return;
            if (attempt < 7) await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidOperationException("GCU 回读未收敛到目标模式");
    }

    private static GpuOutputModeCapabilities Combine(
        GcuGpuOutputModeStatus live,
        GpuOutputModeCapabilities machine) => new(
            machine.SupportsHybrid && live.SupportsDirectSwitch is not false,
            machine.SupportsDiscrete && live.SupportsDirectSwitch is not false,
            machine.SupportsIntegrated && live.SupportsIntegrated is not false);

    private static string Label(GpuOutputMode mode) => mode switch
    {
        GpuOutputMode.Hybrid => "混合输出",
        GpuOutputMode.Discrete => "独显直连",
        GpuOutputMode.Integrated => "纯核显模式",
        _ => mode.ToString()
    };
}
