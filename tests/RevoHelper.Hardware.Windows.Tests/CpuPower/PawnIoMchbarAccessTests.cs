using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class PawnIoMchbarAccessTests
{
    [Fact]
    public void ReadPackageRaplLimit_UsesOnlyTheOfficialReadQwordEntryPoint()
    {
        var module = new FakePawnIoModule(0x1234_5678_9ABC_DEF0UL);
        using var access = new PawnIoMchbarAccess(module);

        var succeeded = access.TryReadPackageRaplLimit(out var value);

        Assert.True(succeeded);
        Assert.Equal(0x1234_5678_9ABC_DEF0UL, value);
        Assert.Equal("ioctl_read_qword", module.LastFunction);
        Assert.Equal([0x59A0UL], module.LastInput);
        Assert.Equal(1, module.LastOutputLength);
    }

    [Fact]
    public void ReadBaseAddress_UsesOnlyTheOfficialAddressEntryPoint()
    {
        var module = new FakePawnIoModule(0xFEDC_0000UL);
        using var access = new PawnIoMchbarAccess(module);

        var succeeded = access.TryReadBaseAddress(out var value);

        Assert.True(succeeded);
        Assert.Equal(0xFEDC_0000UL, value);
        Assert.Equal("ioctl_get_mchbar_addr", module.LastFunction);
        Assert.Empty(module.LastInput);
    }

    private sealed class FakePawnIoModule(ulong readValue) : IPawnIoModule
    {
        public bool IsAvailable => true;
        public string AvailabilityReason => "available";
        public string? LastFunction { get; private set; }
        public ulong[] LastInput { get; private set; } = [];
        public int LastOutputLength { get; private set; }

        public bool TryExecute(
            string function,
            IReadOnlyList<ulong> input,
            int outputLength,
            out ulong[] output)
        {
            LastFunction = function;
            LastInput = input.ToArray();
            LastOutputLength = outputLength;
            output = [readValue];
            return true;
        }

        public void Dispose()
        {
        }
    }
}
