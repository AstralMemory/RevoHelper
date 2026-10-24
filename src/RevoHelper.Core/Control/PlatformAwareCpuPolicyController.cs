namespace RevoHelper.Core.Control;

/// <summary>
/// Keeps OEM profile editing for Custom slots, while preset modes use a verified
/// direct CPU backend so firmware-fixed Office/Balanced/Turbo limits can be overridden.
/// </summary>
public sealed class PlatformAwareCpuPolicyController(
    IPlatformModeController platformController,
    ICpuPolicyController customProfileController,
    ICpuPolicyController? directController) : ICpuPolicyController, ICpuPolicyRestorePointProvider
{
    private readonly Dictionary<PlatformMode, CpuPolicyRestorePoint> presetBaselines = [];
    private readonly SemaphoreSlim presetOperationLock = new(1, 1);

    public bool CanControlPresetPowerLimits => directController switch
    {
        SwitchableCpuPolicyController switchable => switchable.IsAvailable,
        null => false,
        _ => true
    };

    public async ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken)
    {
        var controller = await ResolveControllerAsync(cancellationToken).ConfigureAwait(false);
        return controller is null
            ? null
            : await controller.ReadCpuPolicyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ControlResult> ApplyCpuPolicyAsync(
        CpuPolicy policy,
        CancellationToken cancellationToken)
    {
        var mode = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false);
        if (mode is null)
        {
            return ControlResult.Rejected("无法识别当前整机模式，未写入 CPU 功耗限制。");
        }

        if (mode.Mode == PlatformMode.Custom)
        {
            return await customProfileController.ApplyCpuPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        }

        if (directController is null)
        {
            return ControlResult.Rejected("当前设备没有通过验证的 CPU 功耗直连后端，预设档位保持 OEM 固件限制。");
        }

        await presetOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!presetBaselines.ContainsKey(mode.Mode))
            {
                var baseline = await CpuPolicyRestorePoint.CaptureAsync(directController, cancellationToken).ConfigureAwait(false);
                if (baseline is null)
                {
                    return ControlResult.Rejected("无法保存当前 OEM 档位的原始 CPU 功耗限制，未执行直连覆盖。");
                }

                presetBaselines[mode.Mode] = baseline;
            }

            return await directController.ApplyCpuPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            presetOperationLock.Release();
        }
    }

    public async ValueTask<ControlResult> RestoreActivePresetBaselineAsync(
        CancellationToken cancellationToken)
    {
        if (directController is null)
        {
            return new(true, "没有已启用的 CPU 功耗直连后端，无需恢复。");
        }

        var mode = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false);
        if (mode is null || mode.Mode == PlatformMode.Custom)
        {
            return new(true, "当前不是由直连后端覆盖的 OEM 预设档位，无需恢复。");
        }

        await presetOperationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!presetBaselines.TryGetValue(mode.Mode, out var baseline))
            {
                return new(true, "当前 OEM 预设档位未被本次会话覆盖，无需恢复。");
            }

            var result = await baseline.RestoreAsync(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                presetBaselines.Remove(mode.Mode);
            }

            return result;
        }
        finally
        {
            presetOperationLock.Release();
        }
    }

    public async ValueTask<CpuPolicyRestorePoint?> CaptureRestorePointAsync(CancellationToken cancellationToken)
    {
        var mode = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false);
        var controller = mode?.Mode == PlatformMode.Custom ? customProfileController : directController;
        if (mode is null || controller is null) return null;
        var restorePoint = await CpuPolicyRestorePoint.CaptureAsync(controller, cancellationToken).ConfigureAwait(false);
        return restorePoint is null ? null : new(restorePoint.Policy, async token =>
        {
            var currentMode = await platformController.ReadPlatformModeAsync(token).ConfigureAwait(false);
            return currentMode != mode
                ? ControlResult.Rejected("CPU 功耗恢复目标档案已改变，未写入其它档案。")
                : await restorePoint.RestoreAsync(token).ConfigureAwait(false);
        });
    }

    private async ValueTask<ICpuPolicyController?> ResolveControllerAsync(CancellationToken cancellationToken)
    {
        var mode = await platformController.ReadPlatformModeAsync(cancellationToken).ConfigureAwait(false);
        return mode?.Mode switch
        {
            PlatformMode.Custom => customProfileController,
            null => null,
            _ => directController
        };
    }
}
