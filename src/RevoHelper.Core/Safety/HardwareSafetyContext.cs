using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Safety;

public sealed record HardwareSafetyContext(
    MachineSupportStatus MachineStatus,
    bool AdapterVerified,
    bool FirmwareFanFallbackVerified,
    IReadOnlySet<HardwareOperation>? VerifiedOperations = null);
