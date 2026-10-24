using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class ReadOnlyControlSetTests
{
    [Fact]
    public async Task EveryWrite_IsRejectedWithModuleSpecificReason()
    {
        var controls = new ReadOnlyControlSet("OEM contract is not verified.");

        var results = new[]
        {
            await controls.SetPlatformModeAsync(new(PlatformMode.Balanced), CancellationToken.None),
            await controls.ApplyCpuPolicyAsync(new CpuPolicy(45, 80, 4000), CancellationToken.None),
            await controls.ApplyGpuPolicyAsync(new GpuPolicy(GpuOperatingMode.Hybrid, 120), CancellationToken.None),
            await controls.ApplyFanCurveAsync(new FanCurvePolicy("stock"), CancellationToken.None),
            await controls.ApplyLightingAsync(new LightingPolicy(LightingBehavior.PlatformModeColor), CancellationToken.None),
            await controls.ApplyLiquidCoolingAsync(new LiquidCoolingPolicy(LiquidCoolingStrategy.Quieter), CancellationToken.None)
        };

        Assert.All(results, result =>
        {
            Assert.False(result.Succeeded);
            Assert.Contains("not verified", result.Reason, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(6, controls.Capabilities.Count);
        Assert.All(controls.Capabilities, capability => Assert.False(capability.CanWrite));
    }
}
