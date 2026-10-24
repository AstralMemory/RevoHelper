namespace RevoHelper.Core.Discovery;

public sealed record OemControlInventory(
    IReadOnlyList<OemControlComponent> Components,
    bool HasCompletePlatformModeChain,
    string PlatformModeChain);
