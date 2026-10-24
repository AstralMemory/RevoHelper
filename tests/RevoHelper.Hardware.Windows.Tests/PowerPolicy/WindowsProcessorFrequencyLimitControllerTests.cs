using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.PowerPolicy;

namespace RevoHelper.Hardware.Windows.Tests.PowerPolicy;

public sealed class WindowsProcessorFrequencyLimitControllerTests
{
    private static readonly Guid ActiveScheme = Guid.Parse("381b4222-f694-41f0-9685-ff5bb260df2e");

    [Fact]
    public void MaximumFrequencySetting_UsesDocumentedWindowsGuid() =>
        Assert.Equal(
            Guid.Parse("75b0ae3f-bce0-45a7-8c89-c9611c25e100"),
            WindowsProcessorFrequencyLimitController.MaximumFrequencySetting);

    [Fact]
    public async Task ReadAsync_ReturnsAcAndDcMegahertzFromActiveScheme()
    {
        var api = new FakePowerProfileApi();
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (3100, 2200);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting] = (3200, 2300);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting] = (3300, 2400);
        var controller = new WindowsProcessorFrequencyLimitController(api);

        var policy = await controller.ReadAsync(CancellationToken.None);

        Assert.Equal(new ProcessorFrequencyLimitPolicy(3100, 2200, 3200, 2300, 3300, 2400), policy);
        Assert.Equal(ActiveScheme, api.LastReadScheme);
    }

    [Fact]
    public async Task ApplyAsync_WritesBothValues_ReactivatesAndVerifiesReadback()
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorFrequencyLimitController(api);

        var result = await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(3, api.AcWrites.Count(write => write.Value == 3100));
        Assert.Equal(3, api.DcWrites.Count(write => write.Value == 3100));
        Assert.Equal(ActiveScheme, api.ReactivatedScheme);
    }

    [Fact]
    public async Task ApplyAsync_NonZeroLimit_PersistsRecoveryLeaseBeforeFirstWrite()
    {
        var events = new List<string>();
        var api = new FakePowerProfileApi { Events = events };
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (0, 2100);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting] = (0, 2200);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting] = (0, 2300);
        var store = new FakeLeaseStore(events);
        var controller = new WindowsProcessorFrequencyLimitController(api, store);

        var result = await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.NotNull(store.Lease);
        Assert.Equal(
            new ProcessorFrequencyLimitPolicy(0, 2100, 0, 2200, 0, 2300),
            store.Lease.Original);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(3100), store.Lease.Expected);
        Assert.True(events.IndexOf("save") >= 0 && events.IndexOf("save") < events.IndexOf("write"));
    }

    [Fact]
    public async Task ApplyAsync_SecondLimit_PreservesFirstOriginalBaseline()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);

        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);
        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(4200), CancellationToken.None)).Succeeded);

        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), store.Lease?.Original);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(4200), store.Lease?.Expected);
    }

    [Fact]
    public async Task ApplyAsync_ZeroAfterLimit_KeepsBaselineUntilSessionRecovery()
    {
        var api = new FakePowerProfileApi();
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (2500, 2500);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting] = (2500, 2500);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting] = (2500, 2500);
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);

        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);
        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(0), CancellationToken.None)).Succeeded);

        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(2500), store.Lease?.Original);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), store.Lease?.Expected);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), await controller.ReadAsync(CancellationToken.None));

        Assert.True((await controller.RecoverAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(2500), await controller.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_FirstUnlimitedPolicy_RetainsExistingUserLimitsForExitRecovery()
    {
        var api = new FakePowerProfileApi();
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (2500, 2500);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting] = (2500, 2500);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting] = (2500, 2500);
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);

        var result = await controller.ApplyAsync(ProcessorFrequencyLimitPolicy.Uniform(0), CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.NotNull(store.Lease);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(2500), store.Lease.Original);
        Assert.True((await controller.RecoverAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(2500), await controller.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecoverAsync_AfterNewController_RestoresAllSixValuesAndClearsLease()
    {
        var api = new FakePowerProfileApi();
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (0, 2100);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting] = (0, 2200);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting] = (0, 2300);
        var store = new FakeLeaseStore();
        var original = new ProcessorFrequencyLimitPolicy(0, 2100, 0, 2200, 0, 2300);
        var first = new WindowsProcessorFrequencyLimitController(api, store);
        Assert.True((await first.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);

        var recovered = await new WindowsProcessorFrequencyLimitController(api, store)
            .RecoverAsync(CancellationToken.None);

        Assert.True(recovered.Succeeded, recovered.Reason);
        Assert.Null(store.Lease);
        Assert.Equal(original, await first.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RecoverAsync_PreservesFieldChangedByExternalWriter()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);
        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (2500, 3100);

        var recovered = await controller.RecoverAsync(CancellationToken.None);

        Assert.True(recovered.Succeeded, recovered.Reason);
        var current = await controller.ReadAsync(CancellationToken.None);
        Assert.Equal(2500u, current.AcMaximumMhz);
        Assert.Equal(0u, current.DcMaximumMhz);
        Assert.Equal(0u, current.AcClass1MaximumMhz);
        Assert.Equal(0u, current.DcClass1MaximumMhz);
        Assert.Null(store.Lease);
    }

    [Fact]
    public async Task RecoverAsync_HistoricalScheme_DoesNotActivateThatScheme()
    {
        var otherScheme = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var api = new MultiSchemePowerProfileApi(ActiveScheme);
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);
        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);
        api.ActiveScheme = otherScheme;
        api.ReactivatedSchemes.Clear();

        var recovered = await controller.RecoverAsync(CancellationToken.None);

        Assert.True(recovered.Succeeded, recovered.Reason);
        Assert.DoesNotContain(ActiveScheme, api.ReactivatedSchemes);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), api.ReadPolicy(ActiveScheme));
    }

    [Fact]
    public async Task ApplyAsync_WhenSecondLimitFails_RestoresPriorLeaseSoExitRestoresOriginal()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);
        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);
        api.FailNextWrite = true;

        var failed = await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(4200), CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(3100), store.Lease?.Expected);
        Assert.True((await controller.RecoverAsync(CancellationToken.None)).Succeeded);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), await controller.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAsync_WhenApplyAndRollbackBothPartiallyFail_RecoveryStillOwnsPreviousLimit()
    {
        var api = new FakePowerProfileApi();
        var store = new FakeLeaseStore();
        var controller = new WindowsProcessorFrequencyLimitController(api, store);
        Assert.True((await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100), CancellationToken.None)).Succeeded);
        // Six successful initial writes. Fail during the second apply, and again
        // during its rollback, after one field has changed each time.
        api.FailedWriteNumbers.UnionWith([8, 10]);

        var failed = await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(4200), CancellationToken.None);
        var recovered = await controller.RecoverAsync(CancellationToken.None);

        Assert.False(failed.Succeeded);
        Assert.True(recovered.Succeeded, recovered.Reason);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), await controller.ReadAsync(CancellationToken.None));
        Assert.Null(store.Lease);
    }

    [Fact]
    public async Task ApplyAsync_WhenReadbackDiffers_RestoresPreviousValues()
    {
        var api = new FakePowerProfileApi
        {
            IgnoreWrites = true
        };
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencySetting] = (0, 2400);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting] = (0, 2500);
        api.Values[WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting] = (0, 2600);
        var controller = new WindowsProcessorFrequencyLimitController(api);

        var result = await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(3100),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(6, api.AcWrites.Count);
        Assert.Equal(6, api.DcWrites.Count);
        Assert.Contains("回读", result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(99u)]
    [InlineData(64001u)]
    public async Task ApplyAsync_RejectsOutOfRangeEnabledValue(uint value)
    {
        var api = new FakePowerProfileApi();
        var controller = new WindowsProcessorFrequencyLimitController(api);

        var result = await controller.ApplyAsync(
            ProcessorFrequencyLimitPolicy.Uniform(value),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Empty(api.AcWrites);
        Assert.Empty(api.DcWrites);
    }

    private sealed class FakePowerProfileApi : IPowerProfileApi
    {
        public Dictionary<Guid, (uint Ac, uint Dc)> Values { get; } = [];
        public bool IgnoreWrites { get; init; }
        public bool FailNextWrite { get; set; }
        public HashSet<int> FailedWriteNumbers { get; } = [];
        private int writeNumber;
        public Guid LastReadScheme { get; private set; }
        public Guid? ReactivatedScheme { get; private set; }
        public List<(Guid Setting, uint Value)> AcWrites { get; } = [];
        public List<(Guid Setting, uint Value)> DcWrites { get; } = [];
        public List<string>? Events { get; init; }

        public Guid GetActiveScheme() => ActiveScheme;

        public uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting)
        {
            LastReadScheme = scheme;
            return Values.TryGetValue(setting, out var value) ? value.Ac : 0;
        }

        public uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting)
        {
            LastReadScheme = scheme;
            return Values.TryGetValue(setting, out var value) ? value.Dc : 0;
        }

        public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            if (FailedWriteNumbers.Contains(++writeNumber)) throw new IOException("simulated write failure");
            if (FailNextWrite)
            {
                FailNextWrite = false;
                throw new IOException("simulated partial apply failure");
            }
            Events?.Add("write");
            AcWrites.Add((setting, value));
            if (!IgnoreWrites || AcWrites.Count > 3)
            {
                var old = Values.TryGetValue(setting, out var current) ? current : default;
                Values[setting] = (value, old.Dc);
            }
        }

        public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            if (FailedWriteNumbers.Contains(++writeNumber)) throw new IOException("simulated write failure");
            Events?.Add("write");
            DcWrites.Add((setting, value));
            if (!IgnoreWrites || DcWrites.Count > 3)
            {
                var old = Values.TryGetValue(setting, out var current) ? current : default;
                Values[setting] = (old.Ac, value);
            }
        }

        public void SetActiveScheme(Guid scheme) => ReactivatedScheme = scheme;
    }

    private sealed class FakeLeaseStore(List<string>? events = null) : IProcessorFrequencyLimitLeaseStore
    {
        public ProcessorFrequencyLimitLease? Lease { get; private set; }

        public ValueTask<ProcessorFrequencyLimitLease?> LoadAsync(CancellationToken token) =>
            ValueTask.FromResult(Lease);

        public ValueTask SaveAsync(ProcessorFrequencyLimitLease lease, CancellationToken token)
        {
            events?.Add("save");
            Lease = lease;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken token)
        {
            Lease = null;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MultiSchemePowerProfileApi(Guid activeScheme) : IPowerProfileApi
    {
        private readonly Dictionary<(Guid Scheme, Guid Setting), (uint Ac, uint Dc)> values = [];
        public Guid ActiveScheme { get; set; } = activeScheme;
        public List<Guid> ReactivatedSchemes { get; } = [];

        public Guid GetActiveScheme() => ActiveScheme;
        public uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting) =>
            values.GetValueOrDefault((scheme, setting)).Ac;
        public uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting) =>
            values.GetValueOrDefault((scheme, setting)).Dc;
        public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            var current = values.GetValueOrDefault((scheme, setting));
            values[(scheme, setting)] = (value, current.Dc);
        }
        public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
        {
            var current = values.GetValueOrDefault((scheme, setting));
            values[(scheme, setting)] = (current.Ac, value);
        }
        public void SetActiveScheme(Guid scheme)
        {
            ActiveScheme = scheme;
            ReactivatedSchemes.Add(scheme);
        }

        public ProcessorFrequencyLimitPolicy ReadPolicy(Guid scheme) => new(
            ReadAcValue(scheme, Guid.Empty, WindowsProcessorFrequencyLimitController.MaximumFrequencySetting),
            ReadDcValue(scheme, Guid.Empty, WindowsProcessorFrequencyLimitController.MaximumFrequencySetting),
            ReadAcValue(scheme, Guid.Empty, WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting),
            ReadDcValue(scheme, Guid.Empty, WindowsProcessorFrequencyLimitController.MaximumFrequencyClass1Setting),
            ReadAcValue(scheme, Guid.Empty, WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting),
            ReadDcValue(scheme, Guid.Empty, WindowsProcessorFrequencyLimitController.MaximumFrequencyClass2Setting));
    }
}
