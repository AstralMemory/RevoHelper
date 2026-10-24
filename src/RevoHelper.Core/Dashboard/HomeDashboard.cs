using RevoHelper.Core.Hardware;

namespace RevoHelper.Core.Dashboard;

public sealed record HomeDashboard(
    MachineIdentity Identity,
    IReadOnlyList<PerformanceProfile> Profiles,
    IReadOnlyList<DeviceStatus> Devices);
