namespace RevoHelper.Hardware.Windows.Discovery;

public interface IOemEnvironmentProbe
{
    OemComponentEvidence Inspect(string probeKey);
}

public sealed record OemComponentEvidence(
    bool Present,
    string? Path,
    string? Version,
    string Evidence);
