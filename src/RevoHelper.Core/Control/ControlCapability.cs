namespace RevoHelper.Core.Control;

public sealed record ControlCapability(
    ControlModule Module,
    bool CanRead,
    bool CanWrite,
    string Reason);

public sealed record ControlResult(bool Succeeded, string Reason)
{
    public static ControlResult Rejected(string reason) => new(false, reason);
}
