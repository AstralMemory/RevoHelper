using RevoHelper.Hardware.Windows.Monitoring;

namespace RevoHelper.Hardware.Windows.Tests.Monitoring;

public sealed class PresentMonCsvParserTests
{
    private const string Header = "Application,ProcessID,SwapChainAddress,TimeInSeconds,msBetweenPresents";

    [Theory]
    [InlineData("javaw.exe,42,0xABC,1.000,16.6667")]
    [InlineData("Application.exe,42,0xABC,1.000,16.6667")]
    [InlineData("\"Minecraft, with comma.exe\",42,0xABC,1.000,16.6667")]
    [InlineData("\"A \"\"quoted\"\" name.exe\",\"42\",\"0xABC\",\"1.000\",\"16.6667\"")]
    public void ValidCsv_ExtractsOnlyMeasuredFields(string line)
    {
        var parser = new PresentMonCsvParser();
        Assert.False(parser.TryRead(Header, out _));

        Assert.True(parser.TryRead(line, out var frame));
        Assert.Equal(42, frame.ProcessId);
        Assert.Equal(0xABCul, frame.SwapChainAddress);
        Assert.Equal(1d, frame.TimeInSeconds);
        Assert.Equal(16.6667, frame.MillisecondsBetweenPresents);
    }

    [Theory]
    [InlineData("javaw.exe,42,0x0,1,0")]
    [InlineData("javaw.exe,42,0x0,NaN,10")]
    [InlineData("javaw.exe,42,0x0,1,Infinity")]
    [InlineData("javaw.exe,42,0x0,-1,10")]
    [InlineData("javaw.exe,42,0x0,1,-10")]
    [InlineData("javaw.exe,0,0x0,1,10")]
    [InlineData("javaw.exe,42,wrong,1,10")]
    [InlineData("javaw.exe,42,0x0,1")]
    [InlineData("javaw.exe,42,0x0,1,10,extra")]
    [InlineData("\"unterminated,42,0x0,1,10")]
    [InlineData("\"bad\"x,42,0x0,1,10")]
    [InlineData("warning: no frames available")]
    public void MalformedOrUnavailableRows_AreNotInventedFps(string line)
    {
        var parser = new PresentMonCsvParser();
        parser.TryRead(Header, out _);

        Assert.False(parser.TryRead(line, out _));
    }

    [Fact]
    public void HeaderColumnOrder_IsDiscoveredRatherThanAssumed()
    {
        var parser = new PresentMonCsvParser();
        parser.TryRead("\uFEFF\"Application\",msBetweenPresents,TimeInSeconds,ProcessID,SwapChainAddress,Unused", out _);

        Assert.True(parser.TryRead("javaw.exe,10,2,42,0x0,ignored", out var frame));
        Assert.Equal(42, frame.ProcessId);
        Assert.Equal(10d, frame.MillisecondsBetweenPresents);
    }

    [Fact]
    public void MissingRequiredHeader_IsNotAccepted()
    {
        var parser = new PresentMonCsvParser();
        parser.TryRead("Application,ProcessID,CPUStartTime,MsCPUBusy", out _);

        Assert.False(parser.HasHeader);
        Assert.False(parser.TryRead("app.exe,42,1,10", out _));
    }

    [Fact]
    public void WithoutHeader_RowIsNotAccepted()
    {
        Assert.False(new PresentMonCsvParser().TryRead("javaw.exe,42,0x0,1,10", out _));
    }
}
