using RevoHelper.App.Lifecycle;
using RevoHelper.Core.Control;
using RevoHelper.Core.Control.DynamicPower;
using RevoHelper.Hardware.Windows.PowerPolicy;

namespace RevoHelper.App.Tests.Lifecycle;

public sealed class LayeredFrequencyRecoveryIntegrationTests
{
    [Fact]
    public async Task FailedDynamicRestore_PreservesManualJournalUntilNextLaunchRestoresOriginalFrequency()
    {
        var api = new MemoryPowerApi();
        var manualStore = new MemoryManualStore();
        var manual = new WindowsProcessorFrequencyLimitController(api, manualStore);
        var original = ProcessorFrequencyLimitPolicy.Uniform(0);
        var manualPolicy = new ProcessorFrequencyLimitPolicy(4000, 0, 4000, 0, 4000, 0);
        Assert.Equal(original, await manual.ReadAsync(default));
        Assert.True((await manual.ApplyAsync(manualPolicy, default)).Succeeded);
        var originalManualLease = Assert.IsType<ProcessorFrequencyLimitLease>(manualStore.Lease);
        Assert.Equal(original, originalManualLease.Original);

        // Represent an already-active dynamic overlay with its actual journal shape.
        // Only recovery is under test; no wall-clock idle observation is necessary.
        var dynamicStore = new MemoryDynamicStore
        {
            Lease = new(1, new(api.Scheme, true), new(4000, 4000, 4000), new(3000, 3000, 3000))
        };
        api.SetDynamicOverlay(new(3000, 3000, 3000));
        var dynamicController = new DynamicPowerController(new WindowsDynamicFrequencyBackend(api, () => true), dynamicStore);
        var manualRecoveryCalls = 0;
        api.FailNextAcWrite = true;

        var shutdownRecovered = await LayeredFrequencyRecovery.RunAsync(
            async token =>
            {
                await dynamicController.RecoverAsync(token);
                return !dynamicController.Status.HasRecoveryPending;
            },
            async token =>
            {
                manualRecoveryCalls++;
                return (await manual.RecoverAsync(token)).Succeeded;
            }, default);

        Assert.False(shutdownRecovered);
        Assert.True(dynamicController.Status.HasRecoveryPending);
        Assert.Equal(0, manualRecoveryCalls);
        Assert.Same(originalManualLease, manualStore.Lease);
        Assert.NotNull(dynamicStore.Lease);
        Assert.Equal(new DynamicFrequencyValues(3000, 3000, 3000), api.AcValues);

        // Fresh controllers simulate the next process, sharing only the device and journals.
        var nextDynamic = new DynamicPowerController(new WindowsDynamicFrequencyBackend(api, () => true), dynamicStore);
        var nextManual = new WindowsProcessorFrequencyLimitController(api, manualStore);
        var nextLaunchRecovered = await LayeredFrequencyRecovery.RunAsync(
            async token =>
            {
                await nextDynamic.RecoverAsync(token);
                return !nextDynamic.Status.HasRecoveryPending;
            },
            async token =>
            {
                manualRecoveryCalls++;
                Assert.Null(dynamicStore.Lease);
                Assert.Same(originalManualLease, manualStore.Lease);
                Assert.Equal(new DynamicFrequencyValues(4000, 4000, 4000), api.AcValues);
                return (await nextManual.RecoverAsync(token)).Succeeded;
            }, default);

        Assert.True(nextLaunchRecovered);
        Assert.Equal(1, manualRecoveryCalls);
        Assert.False(nextDynamic.Status.HasRecoveryPending);
        Assert.Null(dynamicStore.Lease);
        Assert.Null(manualStore.Lease);
        Assert.Equal(original, await nextManual.ReadAsync(default));
    }

    private sealed class MemoryPowerApi : IPowerProfileApi
    {
        private static readonly Guid[] FrequencySettings =
        [
            WindowsProcessorFrequencyLimitController.MaximumFrequencySetting,
            WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting,
            WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting
        ];
        private readonly Dictionary<(bool Ac, Guid Setting), uint> values = [];
        public Guid Scheme { get; } = Guid.Parse("d8ca3a90-4bfc-4e2e-8dc0-b225d54a1be7");
        public bool FailNextAcWrite { get; set; }
        public DynamicFrequencyValues AcValues => new(
            values.GetValueOrDefault((true, FrequencySettings[0])),
            values.GetValueOrDefault((true, FrequencySettings[1])),
            values.GetValueOrDefault((true, FrequencySettings[2])));

        public void SetDynamicOverlay(DynamicFrequencyValues overlay)
        {
            for (var index = 0; index < FrequencySettings.Length; index++)
                values[(true, FrequencySettings[index])] = overlay[index];
        }

        public Guid GetActiveScheme() => Scheme;
        public uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting) => values.GetValueOrDefault((true, setting));
        public uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting) => values.GetValueOrDefault((false, setting));
        public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            if (FailNextAcWrite)
            {
                FailNextAcWrite = false;
                throw new IOException("Injected transient failure restoring the dynamic overlay.");
            }
            values[(true, setting)] = value;
        }
        public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value) => values[(false, setting)] = value;
        public void SetActiveScheme(Guid scheme) { }
    }

    private sealed class MemoryManualStore : IProcessorFrequencyLimitLeaseStore
    {
        public ProcessorFrequencyLimitLease? Lease { get; private set; }
        public ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(CancellationToken token) => ValueTask.FromResult(Lease);
        public ValueTask SaveAsync(ProcessorFrequencyLimitLease lease, CancellationToken token)
        { Lease = lease; return ValueTask.CompletedTask; }
        public ValueTask ClearAsync(CancellationToken token)
        { Lease = null; return ValueTask.CompletedTask; }
    }

    private sealed class MemoryDynamicStore : IDynamicPowerLeaseStore
    {
        public DynamicPowerLease? Lease { get; set; }
        public Task<DynamicPowerLease?> LoadAsync(CancellationToken token) => Task.FromResult(Lease);
        public Task SaveAsync(DynamicPowerLease lease, CancellationToken token)
        { Lease = lease; return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken token)
        { Lease = null; return Task.CompletedTask; }
    }
}
