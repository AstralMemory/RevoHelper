using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Settings;

public sealed class LegacyFrequencyLimitMigrationTests
{
    [Fact]
    public async Task RunAsync_MatchingUniformLiveLimit_ClearsLimitAndRetiresLegacyIntent()
    {
        var store = new FakeSettingsStore(SettingsWithLimit("office:1", 4300));
        var controller = new FakeController(ProcessorFrequencyLimitPolicy.Uniform(4300));

        var result = await LegacyFrequencyLimitMigration.RunAsync(
            store, controller, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(ProcessorFrequencyLimitPolicy.Uniform(0), controller.Applied);
        Assert.True(store.Current.FrequencySafetyMigrationCompleted);
        Assert.All(store.Current.PerformanceProfiles!, profile =>
        {
            Assert.False(profile.Value.MaximumProcessorFrequencyConfigured);
            Assert.Equal(0, profile.Value.MaximumProcessorFrequencyMhz);
        });
    }

    [Fact]
    public async Task RunAsync_NonMatchingLiveLimit_PreservesExternalValueButRetiresLegacyIntent()
    {
        var store = new FakeSettingsStore(SettingsWithLimit("office:1", 4300));
        var controller = new FakeController(ProcessorFrequencyLimitPolicy.Uniform(2500));

        var result = await LegacyFrequencyLimitMigration.RunAsync(
            store, controller, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Null(controller.Applied);
        Assert.True(store.Current.FrequencySafetyMigrationCompleted);
        Assert.False(store.Current.PerformanceProfiles!["office:1"].MaximumProcessorFrequencyConfigured);
    }

    [Fact]
    public async Task RunAsync_CompletedMigration_IsIdempotent()
    {
        var settings = SettingsWithLimit("office:1", 4300) with
        {
            FrequencySafetyMigrationCompleted = true
        };
        var store = new FakeSettingsStore(settings);
        var controller = new FakeController(ProcessorFrequencyLimitPolicy.Uniform(4300));

        var result = await LegacyFrequencyLimitMigration.RunAsync(
            store, controller, CancellationToken.None);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Null(controller.Applied);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task RunAsync_ClearFailure_DoesNotMarkMigrationComplete()
    {
        var store = new FakeSettingsStore(SettingsWithLimit("office:1", 4300));
        var controller = new FakeController(ProcessorFrequencyLimitPolicy.Uniform(4300))
        {
            ApplyResult = ControlResult.Rejected("write failed")
        };

        var result = await LegacyFrequencyLimitMigration.RunAsync(
            store, controller, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.False(store.Current.FrequencySafetyMigrationCompleted);
        Assert.Equal(0, store.SaveCount);
    }

    private static ApplicationSettings SettingsWithLimit(string key, int mhz) => new(
        PerformanceProfiles: new Dictionary<string, PerformanceProfileSettings>
        {
            [key] = new(60, 60, ProcessorBoostMode.Aggressive, mhz, true),
            ["balanced:1"] = new(60, 60, ProcessorBoostMode.Aggressive)
        });

    private sealed class FakeSettingsStore(ApplicationSettings initial) : IApplicationSettingsStore
    {
        public ApplicationSettings Current { get; private set; } = initial;
        public int SaveCount { get; private set; }
        public ApplicationSettings Load() => Current;
        public void Save(ApplicationSettings settings)
        {
            Current = settings;
            SaveCount++;
        }
    }

    private sealed class FakeController(ProcessorFrequencyLimitPolicy current)
        : IProcessorFrequencyLimitController
    {
        public ProcessorFrequencyLimitPolicy? Applied { get; private set; }
        public ControlResult ApplyResult { get; init; } = new(true, "ok");
        public ValueTask<ProcessorFrequencyLimitPolicy> ReadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(current);
        public ValueTask<ControlResult> ApplyAsync(
            ProcessorFrequencyLimitPolicy policy,
            CancellationToken cancellationToken)
        {
            Applied = policy;
            return ValueTask.FromResult(ApplyResult);
        }
    }
}
