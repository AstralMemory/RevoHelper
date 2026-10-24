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

        var names = new WindowsDisplayAdapterReader(query).ReadNames();

        Assert.Equal(
            ["Intel(R) Graphics", "NVIDIA GeForce RTX 5090 Laptop GPU"],
            names);
        Assert.Equal("Win32_VideoController", query.RequestedClass);
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
