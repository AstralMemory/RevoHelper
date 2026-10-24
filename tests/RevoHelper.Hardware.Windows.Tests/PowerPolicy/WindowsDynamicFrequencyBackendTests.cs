using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Hardware.Windows.PowerPolicy;

namespace RevoHelper.Hardware.Windows.Tests.PowerPolicy;

public sealed class WindowsDynamicFrequencyBackendTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WritesOnlyRequestedSideClassAndExplicitScheme(bool ac)
    {
        var api = new Api();
        var backend = new WindowsDynamicFrequencyBackend(api, () => ac);
        var context = await backend.GetContextAsync(default);
        Assert.Equal(new(api.Active, ac), context);
        await backend.WriteAsync(context, 1, 3000, default);
        var write = Assert.Single(api.Writes);
        Assert.Equal(context.Scheme, write.Scheme);
        Assert.Equal(ac, write.Ac);
        Assert.Equal(WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting, write.Setting);
        Assert.Equal(3000u, write.Value);
    }

    [Fact]
    public async Task ReadsOldSchemeWithoutActivatingItAndRefreshDoesNotSwitchCurrentScheme()
    {
        var api = new Api();
        var backend = new WindowsDynamicFrequencyBackend(api, () => true);
        var old = new DynamicPowerContext(Guid.NewGuid(), false);
        var values = await backend.ReadAsync(old, default);
        Assert.Equal(new DynamicFrequencyValues(2300, 2300, 2300), values);
        Assert.All(api.Reads, read => { Assert.Equal(old.Scheme, read.Scheme); Assert.False(read.Ac); });
        await backend.RefreshAsync(old, default);
        Assert.Empty(api.Activations);
        await backend.RefreshAsync(new(api.Active, true), default);
        Assert.Equal(api.Active, Assert.Single(api.Activations));
    }

    [Fact]
    public async Task UnknownSupplyNeverAssumesAc()
    {
        var api = new Api();
        var backend = new WindowsDynamicFrequencyBackend(api, () => null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GetContextAsync(default));
        Assert.Empty(api.Writes);
    }

    [Theory]
    [InlineData(-1, 3000u)]
    [InlineData(3, 3000u)]
    [InlineData(0, 99u)]
    [InlineData(0, 64001u)]
    public async Task InvalidClassOrFrequencyNeverWrites(int processorClass, uint value)
    {
        var api = new Api();
        var backend = new WindowsDynamicFrequencyBackend(api, () => true);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => backend.WriteAsync(new(api.Active, true), processorClass, value, default));
        Assert.Empty(api.Writes);
    }

    [Fact]
    public async Task CancelledCallNeverWrites()
    {
        var api = new Api();
        var backend = new WindowsDynamicFrequencyBackend(api, () => true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backend.WriteAsync(new(api.Active, true), 0, 3000, new(true)));
        Assert.Empty(api.Writes);
    }

    private sealed class Api : IPowerProfileApi
    {
        public Guid Active = Guid.NewGuid();
        public List<(Guid Scheme, bool Ac, Guid Setting, uint Value)> Writes { get; } = [];
        public List<(Guid Scheme, bool Ac)> Reads { get; } = [];
        public List<Guid> Activations { get; } = [];
        public Guid GetActiveScheme() => Active;
        public uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting) { Reads.Add((scheme, true)); return 4500; }
        public uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting) { Reads.Add((scheme, false)); return 2300; }
        public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value) => Writes.Add((scheme, true, setting, value));
        public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value) => Writes.Add((scheme, false, setting, value));
        public void SetActiveScheme(Guid scheme) => Activations.Add(scheme);
    }
}
