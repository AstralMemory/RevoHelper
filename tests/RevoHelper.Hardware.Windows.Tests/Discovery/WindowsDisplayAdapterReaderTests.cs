using RevoHelper.Core.Dashboard;
using RevoHelper.Hardware.Windows.Discovery;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class WindowsDisplayAdapterReaderTests
{
    [Fact]
    public void ReadNames_ReturnsEveryNonBlankVideoControllerName()
    {
        var query = new FakeWmiQuery(
        [
            new Dictionary<string, string>
            {
                ["Name"] = "MuMu Virtual Display Adapter",
                ["PNPDeviceID"] = @"ROOT\DISPLAY\0000"
            },
            new Dictionary<string, string>
            {
                ["Name"] = "Intel(R) Graphics",
                ["PNPDeviceID"] = @"PCI\VEN_8086&DEV_7D67"
            },
            new Dictionary<string, string>
            {
                ["Name"] = "NVIDIA GeForce RTX 5090 Laptop GPU",
                ["PNPDeviceID"] = @"PCI\VEN_10DE&DEV_2C18"
            },
            new Dictionary<string, string> { ["Name"] = "" }
        ]);

        var names = new WindowsDisplayAdapterReader(query, _ => null, () => []).ReadNames();

        Assert.Equal(
            ["Intel(R) Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"],
            names);
        Assert.Equal("Win32_VideoController", query.RequestedClass);
    }

    // This machine (2026-11-16): the integrated GPU at PCI bus 0, device 2; the RTX 5090 at bus 2.
    [Fact]
    public void ReadAdapters_TakesTheVendorFromThePciIdAndIntelsIntegratedGpuFromItsBusPosition()
    {
        var query = new FakeWmiQuery(
        [
            new Dictionary<string, string> { ["Name"] = "Intel(R) Graphics", ["PNPDeviceID"] = @"PCI\VEN_8086&DEV_7D67&SUBSYS_50001D05" },
            new Dictionary<string, string> { ["Name"] = "Intel(R) Arc(TM) B390 GPU", ["PNPDeviceID"] = @"PCI\VEN_8086&DEV_B080&SUBSYS_00000000" },
            new Dictionary<string, string> { ["Name"] = "Intel(R) Arc(TM) A370M Graphics", ["PNPDeviceID"] = @"PCI\VEN_8086&DEV_5693&SUBSYS_00000001" },
            new Dictionary<string, string> { ["Name"] = "RTX A1000 Laptop GPU", ["PNPDeviceID"] = @"PCI\VEN_10DE&DEV_25B9" },
            new Dictionary<string, string> { ["Name"] = "AMD Radeon(TM) 8060S Graphics", ["PNPDeviceID"] = @"PCI\VEN_1002&DEV_1586" }
        ]);
        PciLocation? Location(string id) =>
            id.Contains("DEV_7D67") || id.Contains("DEV_B080") ? new PciLocation(0, 2, 0)
            : id.Contains("DEV_5693") ? new PciLocation(3, 0, 0) : null;

        var adapters = new WindowsDisplayAdapterReader(query, Location, () => []).ReadAdapters();

        Assert.Equal(new DisplayAdapter("Intel(R) Graphics", HardwareVendor.Intel, true), adapters[0]);
        // A future integrated GPU named like a discrete one is still integrated: its position says so.
        Assert.Equal(new DisplayAdapter("Intel(R) Arc(TM) B390 GPU", HardwareVendor.Intel, true), adapters[1]);
        Assert.Equal(new DisplayAdapter("Intel(R) Arc(TM) A370M Graphics", HardwareVendor.Intel, false), adapters[2]);
        Assert.Equal(new DisplayAdapter("RTX A1000 Laptop GPU", HardwareVendor.Nvidia, null), adapters[3]);
        Assert.Equal(new DisplayAdapter("AMD Radeon(TM) 8060S Graphics", HardwareVendor.Amd, null), adapters[4]);
    }

    [Fact]
    public void ReadAdapters_WithoutWmiAsksPlugAndPlayDirectly()
    {
        var adapters = new WindowsDisplayAdapterReader(new BrokenWmiQuery(), _ => new PciLocation(0, 2, 0),
            () => [(@"PCI\VEN_8086&DEV_7D67&SUBSYS_50001D05", "Intel(R) Graphics")]).ReadAdapters();

        Assert.Equal([new DisplayAdapter("Intel(R) Graphics", HardwareVendor.Intel, true)], adapters);
    }

    private sealed class BrokenWmiQuery : IWmiQuery
    {
        public IReadOnlyDictionary<string, string> ReadFirst(string className) =>
            throw new System.Runtime.InteropServices.COMException("WMI repository unavailable");

        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className) =>
            throw new System.Runtime.InteropServices.COMException("WMI repository unavailable");
    }

    private sealed class FakeWmiQuery(
        IReadOnlyList<IReadOnlyDictionary<string, string>> values) : IWmiQuery
    {
        public string? RequestedClass { get; private set; }

        public IReadOnlyDictionary<string, string> ReadFirst(string className) => values[0];

        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className)
        {
            RequestedClass = className;
            return values;
        }
    }
}
