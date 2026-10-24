namespace RevoHelper.Core.Control;

public sealed class ReadOnlyControlSet :
    IPlatformModeController,
    ICpuPolicyController,
    IGpuPolicyController,
    IFanCurveController,
    ILightingController,
    ILiquidCoolingController
{
    private readonly string reason;

    public ReadOnlyControlSet(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        this.reason = reason;
        Capabilities = Enum.GetValues<ControlModule>()
            .Select(module => new ControlCapability(module, CanRead: false, CanWrite: false, reason))
            .ToArray();
    }

    public IReadOnlyList<ControlCapability> Capabilities { get; }

    public ValueTask<PlatformModeSelection?> ReadPlatformModeAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<PlatformModeSelection?>(null);
    public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<CpuPolicy?>(null);
    public ValueTask<GpuPolicy?> ReadGpuPolicyAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<GpuPolicy?>(null);
    public ValueTask<FanCurvePolicy?> ReadFanCurveAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<FanCurvePolicy?>(null);
    public ValueTask<LightingPolicy?> ReadLightingAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<LightingPolicy?>(null);
    public ValueTask<LiquidCoolingPolicy?> ReadLiquidCoolingAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<LiquidCoolingPolicy?>(null);

    public ValueTask<ControlResult> SetPlatformModeAsync(PlatformModeSelection selection, CancellationToken cancellationToken) => Reject();
    public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken) => Reject();
    public ValueTask<ControlResult> ApplyGpuPolicyAsync(GpuPolicy policy, CancellationToken cancellationToken) => Reject();
    public ValueTask<ControlResult> ApplyFanCurveAsync(FanCurvePolicy policy, CancellationToken cancellationToken) => Reject();
    public ValueTask<ControlResult> ApplyLightingAsync(LightingPolicy policy, CancellationToken cancellationToken) => Reject();
    public ValueTask<ControlResult> ApplyLiquidCoolingAsync(LiquidCoolingPolicy policy, CancellationToken cancellationToken) => Reject();

    private ValueTask<ControlResult> Reject() => ValueTask.FromResult(ControlResult.Rejected(reason));
}
