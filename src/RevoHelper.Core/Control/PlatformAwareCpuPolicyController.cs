namespace RevoHelper.Core.Control;

/// <summary>
/// Keeps OEM profile editing for Custom slots, while preset modes use a verified
/// direct CPU backend so firmware-fixed Office/Balanced/Turbo limits can be overridden.
/// </summary>
/// <remarks>
/// What a direct write replaced is tracked by the backend for the register as a whole, not per
/// mode: an OEM mode change does not necessarily reprogram the register, so a value read in a
/// later mode can be this session's own earlier write (see <see cref="DirectPowerLimitOwnership{T}"/>).
/// </remarks>
public sealed class PlatformAwareCpuPolicyController(
    IPlatformModeController platformController,
    ICpuPolicyController customProfileController,
    ICpuPolicyController? directController) : ICpuPolicyController, ICpuPolicyRestorePointProvider
{
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

        // The backend reads the register before writing and refuses when it cannot.
        return await directController.ApplyCpuPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Undoes this session's direct PL override at exit, in whatever mode is current. Custom
    /// included: the backend reads the register itself and restores only the fields it wrote,
    /// and only while they still hold its last confirmed write.
    /// </summary>
    public ValueTask<ControlResult> RestoreDirectOverrideAsync(CancellationToken cancellationToken) =>
        directController is ICpuPolicySessionRoot session
            ? session.RestoreSessionRootAsync(cancellationToken)
            : ValueTask.FromResult(new ControlResult(true, "没有可撤回的 CPU 功耗直连覆盖。"));

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
