namespace RevoHelper.Core.Discovery;

public sealed record OemControlComponent(
    string Name,
    string Category,
    bool Present,
    string? Path,
    string? Version,
    string Evidence);
