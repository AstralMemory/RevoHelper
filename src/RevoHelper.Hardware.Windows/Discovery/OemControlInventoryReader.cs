using RevoHelper.Core.Discovery;

namespace RevoHelper.Hardware.Windows.Discovery;

public sealed class OemControlInventoryReader(IOemEnvironmentProbe probe)
{
    public const string Chain =
        "CCU.WinUI -> GCUBridge/GCUService -> ACPIDriverDll -> UWACPIDriver -> ACPI\\INOU0000";

    private static readonly ComponentDefinition[] Definitions =
    [
        new("package:CCU.WinUI", "CCU.WinUI", "Official UI evidence", Required: false),
        new("service:GCUBridge", "GCUBridge", "Privileged service", Required: true),
        new("file:GCUService.exe", "GCUService", "Control service", Required: true),
        new("file:ACPIDriverDll.dll", "ACPIDriverDll", "ACPI bridge", Required: true),
        new("service:UWACPIDriver", "UWACPIDriver", "Kernel driver", Required: true),
        new("pnp:ACPI\\INOU0000", "ACPI\\INOU0000", "Firmware endpoint", Required: true),
        new("directory:UserPofiles", "OEM profile store", "Profile evidence", Required: false),
        new("file:LibreHardwareMonitorLib.dll", "Bundled LibreHardwareMonitor", "Telemetry evidence", Required: false)
    ];

    public OemControlInventory Read()
    {
        var components = Definitions
            .Select(definition => ToComponent(definition, probe.Inspect(definition.ProbeKey)))
            .ToArray();
        var complete = Definitions
            .Select((definition, index) => (definition, component: components[index]))
            .Where(item => item.definition.Required)
            .All(item => item.component.Present);

        return new(components, complete, Chain);
    }

    private static OemControlComponent ToComponent(
        ComponentDefinition definition,
        OemComponentEvidence evidence) =>
        new(
            definition.Name,
            definition.Category,
            evidence.Present,
            evidence.Path,
            evidence.Version,
            evidence.Evidence);

    private sealed record ComponentDefinition(
        string ProbeKey,
        string Name,
        string Category,
        bool Required);
}
