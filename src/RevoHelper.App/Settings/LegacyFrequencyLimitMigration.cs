using RevoHelper.Core.Control;

namespace RevoHelper.App.Settings;

public static class LegacyFrequencyLimitMigration
{
    public static async ValueTask<ControlResult> RunAsync(
        IApplicationSettingsStore settingsStore,
        IProcessorFrequencyLimitController controller,
        CancellationToken cancellationToken)
    {
        var settings = settingsStore.Load();
        if (settings.FrequencySafetyMigrationCompleted)
        {
            return new ControlResult(true, "处理器频率安全迁移已完成。");
        }

        IEnumerable<PerformanceProfileSettings> configuredProfiles =
            settings.PerformanceProfiles?.Values ?? Enumerable.Empty<PerformanceProfileSettings>();
        var legacyTargets = configuredProfiles
            .Where(profile => profile.MaximumProcessorFrequencyConfigured)
            .Select(profile => profile.MaximumProcessorFrequencyMhz)
            .Where(value => value > 0)
            .Select(value => (uint)value)
            .ToHashSet();

        try
        {
            var live = await controller.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (TryGetUniformNonZero(live, out var liveLimit) && legacyTargets.Contains(liveLimit))
            {
                var cleared = await controller.ApplyAsync(
                    ProcessorFrequencyLimitPolicy.Uniform(0), cancellationToken).ConfigureAwait(false);
                if (!cleared.Succeeded)
                {
                    return ControlResult.Rejected(
                        $"旧版处理器频率限制清理失败：{cleared.Reason}");
                }
            }

            Dictionary<string, PerformanceProfileSettings>? profiles = null;
            if (settings.PerformanceProfiles is not null)
            {
                profiles = settings.PerformanceProfiles.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        MaximumProcessorFrequencyMhz = 0,
                        MaximumProcessorFrequencyConfigured = false
                    });
            }

            settingsStore.Save(settings with
            {
                PerformanceProfiles = profiles,
                FrequencySafetyMigrationCompleted = true
            });
            return new ControlResult(true, "旧版处理器频率状态已安全迁移。");
        }
        catch (Exception exception)
        {
            return ControlResult.Rejected($"处理器频率安全迁移失败：{exception.Message}");
        }
    }

    private static bool TryGetUniformNonZero(
        ProcessorFrequencyLimitPolicy policy,
        out uint value)
    {
        value = policy.AcMaximumMhz;
        return value > 0 &&
               policy.DcMaximumMhz == value &&
               policy.AcClass1MaximumMhz == value &&
               policy.DcClass1MaximumMhz == value &&
               policy.AcClass2MaximumMhz == value &&
               policy.DcClass2MaximumMhz == value;
    }
}
