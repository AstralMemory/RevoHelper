using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class GpuPowerBudgetRangeTests
{
    [Theory]
    [InlineData(95, 150, 70)]
    [InlineData(120, 150, 70)]
    [InlineData(50, 100, 50)]
    [InlineData(25, 65, 25)]
    public void ExtendedRangePreservesAlreadyLowerOemRange(int minimum, int maximum, int expected)
    {
        var limits = new GpuPowerLimits(minimum, maximum, 5, 25);
        Assert.Equal(expected, GpuPowerBudgetRange.MinimumTarget(limits));
        Assert.Equal(minimum, limits.TgpMinimum);
        Assert.Equal(maximum, limits.TgpMaximum);
    }
}
