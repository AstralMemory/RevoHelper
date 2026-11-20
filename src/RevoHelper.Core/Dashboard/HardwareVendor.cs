using System.Globalization;

namespace RevoHelper.Core.Dashboard;

public enum HardwareVendor
{
    Unknown,
    Intel,
    Amd,
    Nvidia
}

/// <summary>A display adapter as Windows lists it, with the facts that do not depend on its marketing name.</summary>
/// <param name="Integrated">
/// From the hardware itself when known (an Intel integrated GPU always sits at PCI bus 0, device 2);
/// null leaves the decision to the vendor.
/// </param>
public sealed record DisplayAdapter(string Name, HardwareVendor Vendor = HardwareVendor.Unknown, bool? Integrated = null);

public static class HardwareVendors
{
    public static HardwareVendor FromPciVendorId(int vendorId) => vendorId switch
    {
        0x8086 => HardwareVendor.Intel,
        0x1002 or 0x1022 => HardwareVendor.Amd,
        0x10DE => HardwareVendor.Nvidia,
        _ => HardwareVendor.Unknown
    };

    /// <summary>Windows writes the PCI vendor into every PCI instance ID: PCI\VEN_10DE&amp;DEV_2C18&amp;…</summary>
    public static HardwareVendor FromInstanceId(string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return HardwareVendor.Unknown;
        var index = instanceId.IndexOf("VEN_", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && index + 8 <= instanceId.Length &&
               int.TryParse(instanceId.AsSpan(index + 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vendorId)
            ? FromPciVendorId(vendorId)
            : HardwareVendor.Unknown;
    }

    /// <summary>Only for names with no hardware ID behind them (offline previews, old callers).</summary>
    public static HardwareVendor FromName(string name)
    {
        if (Contains(name, "NVIDIA") || Contains(name, "GeForce") || Contains(name, "Quadro")) return HardwareVendor.Nvidia;
        if (Contains(name, "Radeon") || Contains(name, "AMD")) return HardwareVendor.Amd;
        if (Contains(name, "Intel")) return HardwareVendor.Intel;
        return HardwareVendor.Unknown;

        static bool Contains(string text, string word) => text.Contains(word, StringComparison.OrdinalIgnoreCase);
    }
}
