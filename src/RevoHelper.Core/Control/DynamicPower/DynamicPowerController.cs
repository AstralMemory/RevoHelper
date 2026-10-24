using RevoHelper.Core.Dashboard;
using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Control.DynamicPower;

public sealed class DynamicPowerController : IDynamicPowerController
{
    private const int ObservationWindowSeconds = 600;
    private const int RequiredIdlePercent = 90;
    private readonly IDynamicFrequencyBackend backend;
    private readonly IDynamicPowerLeaseStore store;
    private readonly TimeProvider clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DynamicPowerStatus status = new(false, "动态功耗未启用", 0, false, false);
    private DynamicPowerLease? lease;
    private DateTimeOffset? previousSample;
    private DynamicPowerContext? observationContext;
    private readonly List<ActivitySlice> activityWindow = [];
    private HardwareTelemetrySnapshot? latestSample;
    // Preserve the inventory across idle resets and manual pauses: a disappeared GPU
    // must not silently become optional. Count equal models because telemetry has no device ID.
    private readonly Dictionary<(DeviceKind Kind, string Model), int> knownGpuCounts = [];

    public DynamicPowerController(IDynamicFrequencyBackend backend, IDynamicPowerLeaseStore store, TimeProvider? timeProvider = null)
    {
        this.backend = backend;
        this.store = store;
        clock = timeProvider ?? TimeProvider.System;
    }

    public DynamicPowerStatus Status => Volatile.Read(ref status);

    public Task SetEnabledAsync(bool enabled, CancellationToken token) => enabled
        ? SerializedAsync(async () =>
        {
            if (status.Enabled) return;
            if (status.HasRecoveryPending)
            {
                status = status with { Message = "存在待恢复记录，请先重试恢复。" };
                return;
            }
            await LoadLeaseAsync(token).ConfigureAwait(false);
            if (lease is not null)
            {
                status = status with { Message = "发现待恢复的频率记录，请先恢复后再启用。" };
                return;
            }
            ResetObservation();
            status = new(true, "动态节能观察中：统计最近 10 分钟的忙闲比例。", 0, false, false);
        }, token)
        : PauseAsync("动态功耗已关闭，原频率限制已恢复。", token);

    public Task PauseAsync(string reason, CancellationToken token) => SerializedAsync(async () =>
    {
        ResetObservation();
        status = status with { Enabled = false, IdleSeconds = 0 };
        await RestoreAsync(token).ConfigureAwait(false);
        status = new(false, reason, 0, false, false);
    }, token);

    public Task RecoverAsync(CancellationToken token) => PauseAsync("动态功耗恢复检查完成；默认保持关闭。", token);

    public Task PrepareForExternalChangeAsync(string reason, CancellationToken token) => SerializedAsync(async () =>
    {
        var keepEnabled = status.Enabled;
        ResetObservation();
        await RestoreAsync(token).ConfigureAwait(false);
        status = new(keepEnabled, keepEnabled ? reason : "动态节能未启用。", 0, false, false);
    }, token);

    public Task ObserveAsync(HardwareTelemetrySnapshot sample, CancellationToken token)
    {
        // Publish arrival before the serialized command: a newer queued observation must
        // invalidate a pending cap even while its predecessor is persisting the journal.
        Volatile.Write(ref latestSample, sample);
        return SerializedAsync(() => ObserveCoreAsync(sample, token), token);
    }

    private async Task ObserveCoreAsync(HardwareTelemetrySnapshot sample, CancellationToken token)
    {
        if (!status.Enabled) return;
        // Process every observation for conservative busy/invalid resets. Only cap
        // application is restricted to the latest sample; never erase a queued busy event.
        var evaluation = EvaluateSample(sample);
        if (evaluation.Kind == SampleKind.Invalid)
        {
            ResetObservation();
            await RestoreAsync(token, loadIfMissing: false).ConfigureAwait(false);
            status = new(true, evaluation.Message, 0, false, false);
            return;
        }

        AppendObservation(sample.CapturedAt, evaluation.Kind == SampleKind.Idle);
        var context = await backend.GetContextAsync(token).ConfigureAwait(false);
        DynamicPowerLeaseValidator.ValidateContext(context);
        if (observationContext is not null && observationContext != context)
        {
            await RestoreAsync(token, loadIfMissing: false).ConfigureAwait(false);
            ResetObservation();
            previousSample = sample.CapturedAt;
            observationContext = context;
            status = new(true, "电源方案或供电方式已改变，已恢复原限制并重新观察。", 0, false, false);
            return;
        }
        observationContext ??= context;

        if (lease is not null)
        {
            if (context != lease.Context)
            {
                await RestoreAsync(token, loadIfMissing: false).ConfigureAwait(false);
                ResetObservation();
                status = new(true, "电源方案或供电方式已改变，已恢复原方案；重新观察空闲。", 0, false, false);
                return;
            }
            if (evaluation.Kind == SampleKind.Busy)
            {
                await RestoreAsync(token, loadIfMissing: false).ConfigureAwait(false);
                PublishObservationStatus(evaluation.Message);
                return;
            }
            var current = await backend.ReadAsync(lease.Context, token).ConfigureAwait(false);
            if (current != lease.Expected)
            {
                status = status with { Enabled = false };
                await RestoreAsync(token, loadIfMissing: false).ConfigureAwait(false);
                ResetObservation();
                status = new(false, "检测到外部频率修改，已保留修改并暂停动态功耗。", 0, false, false);
            }
            else
            {
                var (coveredSeconds, idlePercent) = GetObservationProgress();
                status = new(true, "空闲节能中：最高 3000 MHz（不会提高原有更低限制）。",
                    coveredSeconds, true, true, idlePercent);
            }
            return;
        }

        var (seconds, percent) = GetObservationProgress();
        PublishObservationStatus(evaluation.Message);
        if (evaluation.Kind == SampleKind.Busy || seconds < ObservationWindowSeconds || percent < RequiredIdlePercent) return;

        var original = await backend.ReadAsync(context, token).ConfigureAwait(false);
        DynamicPowerLeaseValidator.ValidateValues(original);
        var expected = DynamicPowerLeaseValidator.Cap(original);
        // The durable journal precedes every policy mutation, including partial failures.
        lease = new(1, context, original, expected);
        status = status with { HasRecoveryPending = true };
        await store.SaveAsync(lease, token).ConfigureAwait(false);
        if (await AbandonOutdatedSampleAsync(sample, token).ConfigureAwait(false)) return;
        for (var i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested();
            // Re-check both the active context and the owned value immediately before writes.
            if (await backend.GetContextAsync(token).ConfigureAwait(false) != context)
                throw new InvalidOperationException("应用期间供电方式或电源方案改变，请恢复后重试。");
            var current = await backend.ReadAsync(context, token).ConfigureAwait(false);
            if (current[i] != original[i])
                throw new InvalidOperationException("应用期间频率设置已被外部修改，请恢复后重试。");
            if (await AbandonOutdatedSampleAsync(sample, token).ConfigureAwait(false)) return;
            if (original[i] == expected[i]) continue;
            status = status with { IsLimited = true };
            await backend.WriteAsync(context, i, expected[i], token).ConfigureAwait(false);
        }
        if (await AbandonOutdatedSampleAsync(sample, token).ConfigureAwait(false)) return;
        await backend.RefreshAsync(context, token).ConfigureAwait(false);
        if (await backend.ReadAsync(context, token).ConfigureAwait(false) != expected)
            throw new InvalidDataException("频率限制回读不一致，请恢复后重试。");
        if (await AbandonOutdatedSampleAsync(sample, token).ConfigureAwait(false)) return;
        status = new(true, "空闲节能中：最高 3000 MHz（不会提高原有更低限制）。",
            ObservationWindowSeconds, true, true, percent);
    }

    private async Task<bool> AbandonOutdatedSampleAsync(HardwareTelemetrySnapshot sample, CancellationToken token)
    {
        var age = clock.GetUtcNow() - sample.CapturedAt;
        if (ReferenceEquals(sample, Volatile.Read(ref latestSample)) && age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(3))
            return false;
        var latest = Volatile.Read(ref latestSample);
        await RestoreAsync(token, loadIfMissing: false).ConfigureAwait(false);
        if (ReferenceEquals(sample, latest))
        {
            ResetObservation();
            status = new(true, "遥测已过期，已取消限频并重新观察。", 0, false, false);
        }
        else
        {
            PublishObservationStatus("收到更新的遥测，已取消本次限频。");
        }
        return true;
    }

    private async Task LoadLeaseAsync(CancellationToken token)
    {
        if (lease is not null) return;
        // A malformed/unreadable journal remains pending; never silently discard it.
        status = status with { HasRecoveryPending = true };
        lease = await store.LoadAsync(token).ConfigureAwait(false);
        if (lease is not null) DynamicPowerLeaseValidator.Validate(lease);
        status = status with { HasRecoveryPending = lease is not null };
    }

    private async Task RestoreAsync(CancellationToken token, bool loadIfMissing = true)
    {
        if (loadIfMissing) await LoadLeaseAsync(token).ConfigureAwait(false);
        if (lease is null) return;
        DynamicPowerLeaseValidator.Validate(lease);
        for (var i = 0; i < 3; i++)
        {
            token.ThrowIfCancellationRequested();
            var current = await backend.ReadAsync(lease.Context, token).ConfigureAwait(false);
            // Compare-and-restore: external edits and already-restored fields are not ours.
            if (current[i] != lease.Expected[i] || lease.Original[i] == lease.Expected[i]) continue;
            await backend.WriteAsync(lease.Context, i, lease.Original[i], token).ConfigureAwait(false);
            if ((await backend.ReadAsync(lease.Context, token).ConfigureAwait(false))[i] != lease.Original[i])
                throw new InvalidDataException("原频率限制恢复回读失败。");
        }
        await backend.RefreshAsync(lease.Context, token).ConfigureAwait(false);
        await store.ClearAsync(token).ConfigureAwait(false);
        lease = null;
        status = status with { IsLimited = false, HasRecoveryPending = false };
    }

    private SampleEvaluation EvaluateSample(HardwareTelemetrySnapshot sample)
    {
        var age = clock.GetUtcNow() - sample.CapturedAt;
        if (age < TimeSpan.Zero || age > TimeSpan.FromSeconds(3))
            return SampleEvaluation.Invalid("遥测过期或时间异常，未应用空闲限制。");
        // A fresh snapshot still establishes device presence even when a preceding
        // sampling gap means its load values cannot extend the idle interval.
        var gpus = sample.Devices.Where(device => device.Kind != DeviceKind.Cpu).ToArray();
        var gpuInventoryReason = CheckGpuInventory(gpus);
        if (gpuInventoryReason is not null) return SampleEvaluation.Invalid(gpuInventoryReason);
        if (previousSample is { } previous &&
            (sample.CapturedAt <= previous || sample.CapturedAt - previous > TimeSpan.FromSeconds(3)))
            return SampleEvaluation.Invalid("遥测中断或时间倒退，重新计算观察窗口。");
        var cpus = sample.Devices.Where(device => device.Kind == DeviceKind.Cpu).ToArray();
        if (cpus.Length == 0) return SampleEvaluation.Invalid("缺少 CPU 遥测，无法判断空闲。");
        var isBusy = false;
        var busyMessage = "当前系统忙碌，保持原频率限制。";
        foreach (var cpu in cpus)
        {
            if (!Percent(cpu.UsagePercent)) return SampleEvaluation.Invalid("CPU 总负载遥测无效。");
            if (!Percent(cpu.MaximumCoreUsagePercent)) return SampleEvaluation.Invalid("缺少有效的 CPU 单核负载，无法判断空闲。");
            if (cpu.PowerWatts is not double watts || !double.IsFinite(watts) || watts < 0 || watts > 2000)
                return SampleEvaluation.Invalid("CPU 封装功耗遥测无效。");
            if (cpu.UsagePercent >= 10 || cpu.MaximumCoreUsagePercent >= 40 || watts >= 20)
            {
                isBusy = true;
                busyMessage = "CPU 正在工作，保持原频率限制。";
            }
        }
        foreach (var gpu in gpus)
        {
            if (!Percent(gpu.UsagePercent)) return SampleEvaluation.Invalid("存在 GPU 负载缺失或无效，无法判断空闲。");
            if (gpu.UsagePercent >= 10)
            {
                isBusy = true;
                busyMessage = "GPU 正在工作，保持原频率限制。";
            }
        }
        return isBusy ? SampleEvaluation.Busy(busyMessage) : SampleEvaluation.Idle();
    }

    private string? CheckGpuInventory(IReadOnlyCollection<DeviceTelemetry> gpus)
    {
        if (gpus.Count == 0) return "缺少 GPU 遥测，无法判断空闲。";
        var current = gpus.GroupBy(gpu => (gpu.Kind, gpu.Model))
            .ToDictionary(group => group.Key, group => group.Count());
        var hadInventory = knownGpuCounts.Count != 0;
        var expanded = false;
        foreach (var (identity, count) in current)
        {
            if (!knownGpuCounts.TryGetValue(identity, out var knownCount) || count > knownCount)
            {
                knownGpuCounts[identity] = count;
                expanded = true;
            }
        }
        if (knownGpuCounts.Any(known => !current.TryGetValue(known.Key, out var count) || count < known.Value))
            return "先前检测到的 GPU 遥测缺失，无法判断空闲。";
        if (hadInventory && expanded) return "GPU 遥测列表已改变，重新计算空闲时间。";
        return null;
    }

    private static bool Percent(double? value) => value is double number && double.IsFinite(number) && number is >= 0 and <= 100;

    private void AppendObservation(DateTimeOffset capturedAt, bool isIdle)
    {
        if (previousSample is { } previous)
            activityWindow.Add(new(previous, capturedAt, isIdle));
        previousSample = capturedAt;

        var cutoff = capturedAt - TimeSpan.FromSeconds(ObservationWindowSeconds);
        while (activityWindow.Count > 0 && activityWindow[0].End <= cutoff)
            activityWindow.RemoveAt(0);
        if (activityWindow.Count > 0 && activityWindow[0].Start < cutoff)
            activityWindow[0] = activityWindow[0] with { Start = cutoff };
    }

    private (int CoveredSeconds, int IdlePercent) GetObservationProgress()
    {
        var covered = activityWindow.Sum(slice => (slice.End - slice.Start).TotalSeconds);
        var idle = activityWindow.Where(slice => slice.IsIdle).Sum(slice => (slice.End - slice.Start).TotalSeconds);
        var coveredSeconds = (int)Math.Round(Math.Min(ObservationWindowSeconds, covered), MidpointRounding.AwayFromZero);
        var idlePercent = covered <= 0 ? 0 : (int)Math.Floor(idle * 100d / covered + 0.000001d);
        return (coveredSeconds, Math.Clamp(idlePercent, 0, 100));
    }

    private void PublishObservationStatus(string currentState)
    {
        var (seconds, percent) = GetObservationProgress();
        status = new(true, $"观察中：{seconds}/600 秒，空闲 {percent}% · {currentState}",
            seconds, false, false, percent);
    }

    private void ResetObservation()
    {
        activityWindow.Clear();
        previousSample = null;
        observationContext = null;
    }

    private async Task SerializedAsync(Func<Task> action, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            await action().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ResetObservation();
            status = status with
            {
                Enabled = false,
                IdleSeconds = 0,
                HasRecoveryPending = lease is not null || status.HasRecoveryPending,
                Message = $"动态功耗已暂停：{exception.Message}" +
                    (lease is not null || status.HasRecoveryPending ? " 原值恢复待处理，请重试恢复。" : string.Empty)
            };
            if (exception is OperationCanceledException) throw;
        }
        finally { gate.Release(); }
    }

    private enum SampleKind { Invalid, Busy, Idle }
    private sealed record SampleEvaluation(SampleKind Kind, string Message)
    {
        public static SampleEvaluation Invalid(string message) => new(SampleKind.Invalid, message);
        public static SampleEvaluation Busy(string message) => new(SampleKind.Busy, message);
        public static SampleEvaluation Idle() => new(SampleKind.Idle, "当前空闲。");
    }
    private sealed record ActivitySlice(DateTimeOffset Start, DateTimeOffset End, bool IsIdle);
}
