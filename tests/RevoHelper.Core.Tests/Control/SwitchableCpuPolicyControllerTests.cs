using RevoHelper.Core.Control;

namespace RevoHelper.Core.Tests.Control;

public sealed class SwitchableCpuPolicyControllerTests
{
    [Fact]
    public async Task BeforeProbe_ReadIsNullAndWriteIsRejected()
    {
        using var host = new SwitchableCpuPolicyController();

        Assert.Null(await host.ReadCpuPolicyAsync(CancellationToken.None));
        Assert.False((await host.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task SetController_ActivatesVerifiedBackend()
    {
        using var host = new SwitchableCpuPolicyController();
        var backend = new FakeCpuController(new(45, 65, null));

        host.SetController(backend);
        var result = await host.ApplyCpuPolicyAsync(new(35, 55, null), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(new CpuPolicy(35, 55, null), await host.ReadCpuPolicyAsync(CancellationToken.None));
    }

    private sealed class FakeCpuController(CpuPolicy current) : ICpuPolicyController
    {
        public ValueTask<CpuPolicy?> ReadCpuPolicyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<CpuPolicy?>(current);

        public ValueTask<ControlResult> ApplyCpuPolicyAsync(CpuPolicy policy, CancellationToken cancellationToken)
        {
            current = policy;
            return ValueTask.FromResult(new ControlResult(true, "ok"));
        }
    }
}
