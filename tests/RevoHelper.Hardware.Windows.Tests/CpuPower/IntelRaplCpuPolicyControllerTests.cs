using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.CpuPower;

namespace RevoHelper.Hardware.Windows.Tests.CpuPower;

public sealed class IntelRaplCpuPolicyControllerTests
{
    [Fact]
    public async Task Apply_WritesPackageLimitAndVerifiesTheActualMsr()
    {
        var msr = new FakeMsrAccess(3, 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47));
        var controller = new IntelRaplCpuPolicyController(msr);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(35, 45, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new CpuPolicy(35, 45, null), await controller.ReadCpuPolicyAsync(CancellationToken.None));
        Assert.Single(msr.Writes);
        Assert.Equal(IntelRaplCpuPolicyController.PackagePowerLimitMsr, msr.Writes[0].Register);
    }

    [Fact]
    public async Task Apply_WhenReadbackDoesNotConverge_RestoresOriginalRegister()
    {
        var original = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47);
        var msr = new FakeMsrAccess(3, original) { IgnoreFirstWrite = true };
        var controller = new IntelRaplCpuPolicyController(msr);

        var result = await controller.ApplyCpuPolicyAsync(new CpuPolicy(35, 45, null), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("恢复", result.Reason, StringComparison.Ordinal);
        Assert.Equal(original, msr.PackagePowerLimit);
        Assert.Single(msr.Writes); // The original fields are already intact; no restore write is needed.
    }

    [Fact]
    public async Task Read_WhenBackendUnavailable_ReturnsNull()
    {
        var controller = new IntelRaplCpuPolicyController(new FakeMsrAccess(3, 0) { IsAvailable = false });

        Assert.Null(await controller.ReadCpuPolicyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Read_WhenFirmwareLockedPackagePowerLimit_ReturnsNullSoBackendIsNotExposed()
    {
        var locked = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47) | (1UL << 63);
        var controller = new IntelRaplCpuPolicyController(new FakeMsrAccess(3, locked));

        Assert.Null(await controller.ReadCpuPolicyAsync(CancellationToken.None));
    }

    [Fact]
    public async Task VerifyWriteAccess_WritesTheExactOriginalRegisterAndReadsItBack()
    {
        var original = 360UL | (1UL << 15) | (520UL << 32) | (1UL << 47);
        var msr = new FakeMsrAccess(3, original);
        var controller = new IntelRaplCpuPolicyController(msr);

        var result = await controller.VerifyWriteAccessAsync(CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Single(msr.Writes);
        Assert.Equal(original, msr.Writes[0].Value);
        Assert.Equal(original, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePoint_RestoresDisabledEnableBitsAndFractionalPowerEncoding()
    {
        var original = 361UL | (523UL << 32);
        var msr = new FakeMsrAccess(3, original);
        var controller = new IntelRaplCpuPolicyController(msr);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);

        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), default)).Succeeded);
        Assert.True((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal(original, msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePoint_KeepsCurrentUncontrolledBits()
    {
        var original = 360UL | (520UL << 32);
        var msr = new FakeMsrAccess(3, original);
        var controller = new IntelRaplCpuPolicyController(msr);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), default)).Succeeded);
        msr.SetExternal(msr.PackagePowerLimit | (1UL << 17));

        Assert.True((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal(original | (1UL << 17), msr.PackagePowerLimit);
    }

    [Fact]
    public async Task RestorePoint_ExternalPowerChangeIsPreserved()
    {
        var msr = new FakeMsrAccess(3, 360UL | (520UL << 32));
        var controller = new IntelRaplCpuPolicyController(msr);
        var baseline = await CpuPolicyRestorePoint.CaptureAsync(controller, default);
        Assert.True((await controller.ApplyCpuPolicyAsync(new(35, 45, null), default)).Succeeded);
        var external = IntelRaplPowerLimitCodec.Encode(3, msr.PackagePowerLimit, 40, 50);
        msr.SetExternal(external);

        Assert.False((await baseline!.RestoreAsync(default)).Succeeded);
        Assert.Equal(external, msr.PackagePowerLimit);
        Assert.Single(msr.Writes);
    }

    private sealed class FakeMsrAccess(ulong powerUnit, ulong packagePowerLimit) : IMsrAccess
    {
        private int writeCount;
        public bool IsAvailable { get; init; } = true;
        public string AvailabilityReason => IsAvailable ? "available" : "unavailable";
        public bool IgnoreFirstWrite { get; init; }
        public ulong PackagePowerLimit { get; private set; } = packagePowerLimit;
        public List<(uint Register, ulong Value)> Writes { get; } = [];
        public void SetExternal(ulong value) => PackagePowerLimit = value;

        public bool TryRead(uint register, out ulong value)
        {
            value = register switch
            {
                IntelRaplCpuPolicyController.RaplPowerUnitMsr => powerUnit,
                IntelRaplCpuPolicyController.PackagePowerLimitMsr => PackagePowerLimit,
                _ => 0
            };
            return IsAvailable && register is IntelRaplCpuPolicyController.RaplPowerUnitMsr
                or IntelRaplCpuPolicyController.PackagePowerLimitMsr;
        }

        public bool TryWrite(uint register, ulong value)
        {
            if (!IsAvailable || register != IntelRaplCpuPolicyController.PackagePowerLimitMsr) return false;
            Writes.Add((register, value));
            writeCount++;
            if (!(IgnoreFirstWrite && writeCount == 1)) PackagePowerLimit = value;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
