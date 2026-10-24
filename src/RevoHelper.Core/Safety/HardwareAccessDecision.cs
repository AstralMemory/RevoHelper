namespace RevoHelper.Core.Safety;

public sealed record HardwareAccessDecision(bool Allowed, string Reason);
