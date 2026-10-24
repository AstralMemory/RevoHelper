using RevoHelper.Hardware.Windows.Discovery;
using RevoHelper.Core.Hardware;

namespace RevoHelper.Hardware.Windows.Tests.Discovery;

public sealed class WindowsMachineIdentityReaderTests
{
    [Fact]
    public void Read_MapsComputerCpuBiosAndBaseboardValues()
    {
        var query = new FakeWmiQuery(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["Win32_ComputerSystem"] = new Dictionary<string, string>
            {
                ["Manufacturer"] = "MECHREVO",
                ["Model"] = "YAOSHI Series"
            },
            ["Win32_Processor"] = new Dictionary<string, string>
            {
                ["Name"] = "Intel(R) Core(TM) Ultra 9 275HX",
                ["Manufacturer"] = "GenuineIntel",
                ["Architecture"] = "9",
                ["NumberOfCores"] = "24",
                ["NumberOfLogicalProcessors"] = "24"
            },
            ["Win32_BIOS"] = new Dictionary<string, string>
            {
                ["SMBIOSBIOSVersion"] = "N.1.18MRO24"
            },
            ["Win32_BaseBoard"] = new Dictionary<string, string>
            {
                ["Product"] = "YAOSHI Series-X6AR55xY"
            }
        });

        var identity = new WindowsMachineIdentityReader(
            query,
            new FakeCpuIdReader(new("GenuineIntel", 6, 198, 2))).Read();

        Assert.Equal("MECHREVO", identity.Manufacturer);
        Assert.Equal("YAOSHI Series", identity.ProductName);
        Assert.Equal("YAOSHI Series-X6AR55xY", identity.BaseboardProduct);
        Assert.Equal("Intel(R) Core(TM) Ultra 9 275HX", identity.ProcessorName);
        Assert.Equal(24, identity.ProcessorCoreCount);
        Assert.Equal(24, identity.ProcessorThreadCount);
        Assert.Equal("N.1.18MRO24", identity.BiosVersion);
        Assert.Equal(CpuVendor.Intel, identity.Cpu.Vendor);
        Assert.Equal("GenuineIntel", identity.Cpu.VendorId);
        Assert.Equal(6, identity.Cpu.Family);
        Assert.Equal(198, identity.Cpu.Model);
        Assert.Equal(2, identity.Cpu.Stepping);
        Assert.Equal(9, identity.Cpu.Architecture);
    }

    [Fact]
    public void Read_WhenCpuIdIsUnavailable_UsesManufacturerWithoutGuessingFromMarketingName()
    {
        var query = new FakeWmiQuery(new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["Win32_ComputerSystem"] = new Dictionary<string, string>(),
            ["Win32_Processor"] = new Dictionary<string, string>
            {
                ["Name"] = "AMD Ryzen Processor",
                ["Manufacturer"] = "AuthenticAMD",
                ["Architecture"] = "9"
            },
            ["Win32_BIOS"] = new Dictionary<string, string>(),
            ["Win32_BaseBoard"] = new Dictionary<string, string>()
        });

        var identity = new WindowsMachineIdentityReader(query, new FakeCpuIdReader(null)).Read();

        Assert.Equal(CpuVendor.Amd, identity.Cpu.Vendor);
        Assert.Equal("AuthenticAMD", identity.Cpu.VendorId);
        Assert.Equal(0, identity.Cpu.Family);
        Assert.Equal(0, identity.Cpu.Model);
        Assert.Equal(0, identity.Cpu.Stepping);
    }

    private sealed class FakeCpuIdReader(CpuIdSignature? signature) : ICpuIdReader
    {
        public CpuIdSignature? Read() => signature;
    }

    private sealed class FakeWmiQuery(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> values) : IWmiQuery
    {
        public IReadOnlyDictionary<string, string> ReadFirst(string className) => values[className];

        public IReadOnlyList<IReadOnlyDictionary<string, string>> ReadAll(string className) =>
            [values[className]];
    }
}
