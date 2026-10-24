using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class PawnIoMsrAccessTests
{
    [Fact]
    public void Read_UsesTheSignedIntelModuleReadEntryPoint()
    {
        var module = new FakePawnIoModule { ReadValue = 0x1234_5678_9ABC_DEF0UL };
        using var access = new PawnIoMsrAccess(module);

        var succeeded = access.TryRead(0x610, out var value);

        Assert.True(succeeded);
        Assert.Equal(0x1234_5678_9ABC_DEF0UL, value);
        Assert.Equal("ioctl_read_msr", module.LastFunction);
        Assert.Equal([0x610UL], module.LastInput);
        Assert.Equal(1, module.LastOutputLength);
    }

    [Fact]
    public void Write_UsesTheAllowListedPackagePowerLimitEntryPoint()
    {
        var module = new FakePawnIoModule();
        using var access = new PawnIoMsrAccess(module);

        var succeeded = access.TryWrite(0x610, 0xAABB_CCDD_EEFF_0011UL);

        Assert.True(succeeded);
        Assert.Equal("ioctl_write_msr", module.LastFunction);
        Assert.Equal([0x610UL, 0xAABB_CCDD_EEFF_0011UL], module.LastInput);
        Assert.Equal(0, module.LastOutputLength);
    }

    [Fact]
    public void UnavailableModule_RejectsReadsAndWrites()
    {
        var module = new FakePawnIoModule { IsAvailable = false };
        using var access = new PawnIoMsrAccess(module);

        Assert.False(access.TryRead(0x606, out _));
        Assert.False(access.TryWrite(0x610, 1));
        Assert.Contains("test unavailable", access.AvailabilityReason, StringComparison.Ordinal);
    }

    private sealed class FakePawnIoModule : IPawnIoModule
    {
        public bool IsAvailable { get; init; } = true;
        public string AvailabilityReason => IsAvailable ? "available" : "test unavailable";
        public ulong ReadValue { get; init; }
        public string? LastFunction { get; private set; }
        public ulong[] LastInput { get; private set; } = [];
        public int LastOutputLength { get; private set; }

        public bool TryExecute(string function, IReadOnlyList<ulong> input, int outputLength, out ulong[] output)
        {
            LastFunction = function;
            LastInput = input.ToArray();
            LastOutputLength = outputLength;
            output = outputLength == 0 ? [] : [ReadValue];
            return IsAvailable;
        }

        public void Dispose()
        {
        }
    }
}
