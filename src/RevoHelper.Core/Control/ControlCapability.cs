namespace RevoHelper.Core.Control;

public sealed record ControlCapability(
    ControlModule Module,
    bool CanRead,
    bool CanWrite,
    string Reason);

public sealed record ControlResult(bool Succeeded, string Reason)
{
    // A requested sub-strategy can fail while its basic mode remains confirmed.
    // Keep that observed state separate from overall request success.
    public PlatformModeSelection? ConfirmedPlatform { get; init; }

    public static ControlResult Rejected(string reason) => new(false, reason);
}
