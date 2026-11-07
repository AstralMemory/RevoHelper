using System.Diagnostics;
using RevoHelper.App.Settings;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

// The lighting drawer: each zone keeps its own queue of the newest choice (LatestWrite),
// the editor shows it at once, and readback confirms or reports it in the background.
public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<LightingZone, LightingLocalTarget> lightingLocalTargets = [];
    private readonly Dictionary<LightingZone, LightingZoneConfiguration> lightingEditorDrafts = [];
    private readonly Dictionary<LightingZone, string> lightingZoneFeedback = [];
    private readonly Dictionary<LightingZone, LightingZoneState> lightingZoneStates = [];
    private LightingProfile? importedLightingProfile;
    private LightingZone selectedLightingZone = LightingZone.Keyboard;
    private LightingEffect selectedLightingEffect = LightingEffect.OemDefault;
    private LightingDirection selectedLightingDirection = LightingDirection.Left;
    private readonly Dictionary<LightingZone, LatestWrite> lightingWrites = [];
    private readonly HashSet<LightingZone> lightingPowerRequests = [];
    private readonly HashSet<LightingZone> lightingProblemZones = [];
    private LatestWrite? lightingSleepWrite;

    public bool IsKeyboardLightingEnabled
    {
        get => isKeyboardLightingEnabled;
        private set { if (isKeyboardLightingEnabled == value) return; isKeyboardLightingEnabled = value; OnPropertyChanged(); }
    }

    public LightingZone SelectedLightingZone
    {
        get => selectedLightingZone;
        set
        {
            if (selectedLightingZone == value) return;
            selectedLightingZone = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedLightingZoneLabel));
            OnPropertyChanged(nameof(IsKeyboardLightingZoneSelected));
            OnPropertyChanged(nameof(CanSelectLightingDirection));
            OnPropertyChanged(nameof(ShowLightingBrightness));
            OnPropertyChanged(nameof(ShowLightingSpeed));
            OnPropertyChanged(nameof(IsLightingStatusProblem));
            OnPropertyChanged(nameof(CanEditLightingColor));
            OnPropertyChanged(nameof(IsWaterDynamicLightingEffectSelected));
            OnPropertyChanged(nameof(IsFirmwarePaletteLightingEffectSelected));
            OnPropertyChanged(nameof(IsOemDefaultLightingEffectSelected));
            LoadLightingEditor(value);
            LightingStatusText = lightingZoneFeedback.GetValueOrDefault(value) ??
                $"{LightingZoneLabel(value)}：调整后自动应用，仅控制当前分区。";
        }
    }

    public string SelectedLightingZoneLabel => LightingZoneLabel(SelectedLightingZone);

    public LightingEffect SelectedLightingEffect
    {
        get => selectedLightingEffect;
        set
        {
            if (selectedLightingEffect == value) return;
            selectedLightingEffect = value;
            RememberLightingEditorDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectLightingDirection));
            OnPropertyChanged(nameof(ShowLightingSpeed));
            OnPropertyChanged(nameof(CanEditLightingColor));
            OnPropertyChanged(nameof(IsWaterDynamicLightingEffectSelected));
            OnPropertyChanged(nameof(IsFirmwarePaletteLightingEffectSelected));
            OnPropertyChanged(nameof(IsOemDefaultLightingEffectSelected));
        }
    }

    public bool IsSelectedLightingZoneEnabled
    {
        get => isSelectedLightingZoneEnabled;
        set
        {
            if (isSelectedLightingZoneEnabled == value) return;
            isSelectedLightingZoneEnabled = value;
            RememberLightingEditorDraft();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LightingEditorApplyHint));
            OnPropertyChanged(nameof(ShowLightingOffHint));
        }
    }

    public string LightingEditorApplyHint => IsSelectedLightingZoneEnabled
        ? "调整后自动应用并回读"
        : "灯区已关闭：参数仅保存，打开后自动应用";

    public string LightingColorHex
    {
        get => lightingColorHex;
        set { if (lightingColorHex == value) return; lightingColorHex = value; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public int LightingBrightness
    {
        get => lightingBrightness;
        set { var clamped = Math.Clamp(value, 0, 4); if (lightingBrightness == clamped) return; lightingBrightness = clamped; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public int LightingSpeed
    {
        get => lightingSpeed;
        set { var clamped = Math.Clamp(value, 1, 4); if (lightingSpeed == clamped) return; lightingSpeed = clamped; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public LightingDirection SelectedLightingDirection
    {
        get => selectedLightingDirection;
        set { if (selectedLightingDirection == value) return; selectedLightingDirection = value; RememberLightingEditorDraft(); OnPropertyChanged(); }
    }

    public int LightingSleepSeconds
    {
        get => lightingSleepSeconds;
        set { if (lightingSleepSeconds == value) return; lightingSleepSeconds = value; OnPropertyChanged(); }
    }

    public bool CanApplyLighting => quickControlsController is not null &&
                                    lightingZoneStates.GetValueOrDefault(SelectedLightingZone)?.Available == true &&
                                    (SelectedLightingZone != LightingZone.Keyboard || !IsKeyboardLightingPowerOnly);

    public bool CanToggleSelectedLightingZonePower => quickControlsController is not null &&
                                    lightingZoneStates.GetValueOrDefault(SelectedLightingZone)?.Available == true;

    public bool CanApplyLightingSleepTimer => quickControlsController is not null &&
                                               (IsKeyboardLightingZoneAvailable && !IsKeyboardLightingPowerOnly || IsCoastLightingZoneAvailable);

    public bool IsSynchronizingLightingEditor { get; private set; }

    public bool IsKeyboardLightingZoneSelected => SelectedLightingZone == LightingZone.Keyboard;

    public bool CanSelectLightingDirection => IsKeyboardLightingZoneSelected && SelectedLightingEffect == LightingEffect.Wave;

    // Water cooler commands carry neither value, and a static effect has no speed.
    public bool ShowLightingBrightness => SelectedLightingZone != LightingZone.LiquidCooler;

    public bool ShowLightingSpeed => ShowLightingBrightness && SelectedLightingEffect != LightingEffect.Static;

    public bool ShowLightingOffHint => !IsSelectedLightingZoneEnabled;

    public bool IsLightingStatusProblem => lightingProblemZones.Contains(SelectedLightingZone);

    public bool IsWaterDynamicLightingEffectSelected =>
        SelectedLightingZone == LightingZone.LiquidCooler && SelectedLightingEffect != LightingEffect.Static;

    public bool IsOemDefaultLightingEffectSelected => SelectedLightingEffect == LightingEffect.OemDefault;

    public bool IsFirmwarePaletteLightingEffectSelected =>
        SelectedLightingZone != LightingZone.LiquidCooler &&
        !IsOemDefaultLightingEffectSelected &&
        !UsesUserLightingColor(SelectedLightingEffect);

    public bool CanEditLightingColor =>
        !IsOemDefaultLightingEffectSelected &&
        !IsWaterDynamicLightingEffectSelected &&
        !IsFirmwarePaletteLightingEffectSelected;

    public string LightingStatusText
    {
        get => lightingStatusText;
        private set
        {
            lightingZoneFeedback[SelectedLightingZone] = value;
            if (lightingStatusText == value) return;
            lightingStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsKeyboardLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.Keyboard);
    public bool IsCoastLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.CoastLightbar);
    public bool IsLogoLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.Logo);
    public bool IsWaterLightingZoneAvailable => IsLightingZoneAvailable(LightingZone.LiquidCooler);
    public async Task OpenLightingAsync(CancellationToken cancellationToken)
    {
        ActiveDrawer = HomeDrawer.Lighting;
        if (isReadingLighting) return;
        if (quickControlsController is null)
        {
            LightingStatusText = "官方灯光控制通道未接入。";
            return;
        }

        isReadingLighting = true;
        try
        {
            // Each zone appears as soon as it answers, so one slow zone cannot hold back the others.
            await Task.WhenAll(Enum.GetValues<LightingZone>().Select(async zone =>
            {
                var started = Stopwatch.GetTimestamp();
                LightingZoneState? state;
                try
                {
                    state = await quickControlsController.ReadLightingZoneAsync(zone, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    state = new(false, exception.Message, null, []);
                }
                TraceControl("lighting", $"read zone={zone} available={state?.Available == true} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0}");
                ApplyLightingZoneState(zone, state ?? new(false, null, null, []));
                if (!IsLightingZoneAvailable(SelectedLightingZone) && IsLightingZoneAvailable(zone))
                    SelectedLightingZone = zone;
            }));
            // Older keyboards can report a plain backlight switch without a detailed RGB zone.
            // Discover that switch independently; missing RGB state must not remove its sole power control.
            if (!IsKeyboardLightingZoneAvailable)
            {
                try
                {
                    ApplyKeyboardLighting(await quickControlsController.ReadKeyboardLightingAsync(cancellationToken));
                }
                catch (Exception exception) when (exception is not OperationCanceledException) { }
            }
            if (lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration is { } keyboard)
                IsKeyboardLightingEnabled = keyboard.Enabled;
            NotifyLightingCapabilitiesChanged();
            var firstAvailable = Enum.GetValues<LightingZone>().FirstOrDefault(IsLightingZoneAvailable);
            SelectedLightingZone = IsLightingZoneAvailable(SelectedLightingZone) ? SelectedLightingZone : firstAvailable;
            LoadLightingEditor(SelectedLightingZone);
            var count = lightingZoneStates.Values.Count(state => state.Available);
            LightingStatusText = count == 0
                ? "未收到灯光状态；请确认 GCUBridge 正在运行后重试。"
                : $"已发现 {count} 个灯光分区；调整后自动应用。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LightingStatusText = $"读取灯光能力失败：{exception.Message}";
        }
        finally
        {
            isReadingLighting = false;
        }
    }

    public void ApplyLightingZoneState(LightingZone zone, LightingZoneState state)
    {
        if (lightingLocalTargets.TryGetValue(zone, out var localTarget))
        {
            if (utcNow() >= localTarget.ExpiresAt)
            {
                lightingLocalTargets.Remove(zone);
            }
            else if (!state.HasDetailedConfiguration ||
                     !LightingConfigurationsMatch(state.Configuration, localTarget.Configuration))
            {
                var previous = lightingZoneStates.GetValueOrDefault(zone);
                lightingZoneStates[zone] = LightingStateAccumulator.Merge(previous, state)! with
                {
                    HardwareType = state.HardwareType ?? previous?.HardwareType,
                    Configuration = localTarget.Configuration,
                    SupportedEffects = state.SupportedEffects.Count > 0
                        ? state.SupportedEffects
                        : previous?.SupportedEffects ?? [localTarget.Configuration.Effect]
                };
                if (zone == LightingZone.Keyboard)
                    IsKeyboardLightingEnabled = localTarget.Configuration.Enabled;
                NotifyLightingCapabilitiesChanged();
                return;
            }
            // Keep the local target for the complete settlement window. One matching acknowledgement
            // does not prove that an older queued MQTT packet cannot still arrive afterward.
        }

        var previousState = lightingZoneStates.GetValueOrDefault(zone);
        state = LightingStateAccumulator.Merge(previousState, state)!;
        lightingZoneStates[zone] = state;
        if (zone == LightingZone.Keyboard && state.Configuration is { } keyboard)
            IsKeyboardLightingEnabled = keyboard.Enabled;
        NotifyLightingCapabilitiesChanged();
        if (SelectedLightingZone != zone) return;

        // A user edit is intent, not hardware evidence. Keep it intact until that exact
        // immutable request completes; an older acknowledgement must not edit the next request.
        if (lightingEditorDrafts.ContainsKey(zone)) return;

        LoadLightingEditor(zone);
        LightingStatusText = $"{LightingZoneLabel(zone)}已通过 GCU 状态事件同步。";
    }

    public Task SetKeyboardLightingAsync(bool enabled, CancellationToken cancellationToken) =>
        SetKeyboardLightingAsync(CaptureKeyboardLightingPower(enabled), cancellationToken);

    public LightingZoneConfiguration CaptureKeyboardLightingPower(bool enabled)
    {
        var target = BuildLightingDraft("keyboard-power", "keyboard-power").Zones[0] with { Enabled = enabled };
        if (lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration is null)
            target = target with { Effect = LightingEffect.OemDefault };
        lightingEditorDrafts[LightingZone.Keyboard] = target;
        if (SelectedLightingZone == LightingZone.Keyboard) LoadLightingEditor(LightingZone.Keyboard);
        return target;
    }

    public Task SetKeyboardLightingAsync(LightingZoneConfiguration target, CancellationToken cancellationToken) =>
        SetLightingZonePowerAsync(target, cancellationToken);

    public void SelectLightingZone(LightingZone zone)
    {
        if (IsLightingZoneAvailable(zone)) SelectedLightingZone = zone;
    }

    public void SetLightingColor(string hex)
    {
        if (TryParseLightingColor(hex, out var color)) LightingColorHex = color.Hex;
    }

    public Task ApplyOemDefaultLightingAsync(CancellationToken cancellationToken) =>
        ApplyLightingProfileAsync(LightingProfileCatalog.OemDefault, cancellationToken);

    public Task SetSelectedLightingZonePowerAsync(CancellationToken cancellationToken)
    {
        var target = CaptureLightingEditor();
        return target is null ? Task.CompletedTask : SetLightingZonePowerAsync(target, cancellationToken);
    }

    public async Task SetLightingZonePowerAsync(LightingZoneConfiguration targetConfiguration, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        var started = Stopwatch.GetTimestamp();
        var zone = targetConfiguration.Zone;
        var enabled = targetConfiguration.Enabled;
        var useLegacyKeyboardPower = zone == LightingZone.Keyboard &&
            (!IsKeyboardLightingZoneAvailable || IsKeyboardLightingPowerOnly);
        var hardwareTarget = useLegacyKeyboardPower
            ? targetConfiguration with { Effect = LightingEffect.OemDefault }
            : targetConfiguration;
        var previousEnabled = lightingZoneStates.GetValueOrDefault(zone)?.Configuration?.Enabled;
        try
        {
            ControlResult result;
            TrackLightingLocalTarget(hardwareTarget);
            if (useLegacyKeyboardPower)
            {
                // Choose one protocol path, never send both the zone command and its legacy equivalent.
                result = await quickControlsController.SetKeyboardLightingAsync(enabled, cancellationToken);
            }
            else if (enabled && (zone != LightingZone.Keyboard || lightingEditorDrafts.ContainsKey(zone)))
            {
                result = await quickControlsController.ApplyLightingZoneAsync(targetConfiguration, cancellationToken);
            }
            else
            {
                result = await quickControlsController.SetLightingZonePowerAsync(zone, enabled, cancellationToken);
            }
            TraceLightingWrite("power", targetConfiguration, started, result);
            if (result.Succeeded)
            {
                if (useLegacyKeyboardPower)
                    StoreKeyboardLightingPower(hardwareTarget, lightingZoneStates.GetValueOrDefault(zone)?.HardwareType);
                else
                    StoreLightingZoneConfiguration(targetConfiguration);
            }
            if (zone == LightingZone.Keyboard) KeyboardControlsStatusText = result.Reason;
            ReportLightingFeedback(zone,
                lightingEditorDrafts.TryGetValue(zone, out var newer) && newer != targetConfiguration
                    ? $"{LightingZoneLabel(zone)}已保留最新选择，等待应用。"
                    : result.Reason);
            StatusMessage = result.Reason;
            if (result.Succeeded)
            {
                if (result.UnconfirmedDetail is null) CompleteLightingEditorDraft(targetConfiguration);
                TrackLightingLocalTarget(hardwareTarget);
                if (useLegacyKeyboardPower && SelectedLightingZone == zone) LoadLightingEditor(zone);
            }
            else if (!result.Succeeded)
                lightingLocalTargets.Remove(zone);
            if (!result.Succeeded && previousEnabled is { } value &&
                lightingEditorDrafts.GetValueOrDefault(zone) == targetConfiguration)
            {
                lightingEditorDrafts[zone] = targetConfiguration with { Enabled = value };
                if (SelectedLightingZone == zone) LoadLightingEditor(zone);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lightingLocalTargets.Remove(zone);
            var reason = $"切换{LightingZoneLabel(zone)}失败：{exception.Message}";
            if (zone == LightingZone.Keyboard) KeyboardControlsStatusText = reason;
            MarkLightingProblem(zone, true);
            ReportLightingFeedback(zone, reason);
        }
    }

    public Task ApplySelectedLightingZoneAsync(CancellationToken cancellationToken)
    {
        var configuration = CaptureLightingEditor();
        return configuration is null ? Task.CompletedTask : ApplyLightingConfigurationAsync(configuration, cancellationToken);
    }

    public LightingZoneConfiguration? CaptureLightingEditor()
    {
        if (TryBuildSelectedLightingConfiguration(out var configuration)) return configuration;
        LightingStatusText = "颜色格式无效，请输入 #RRGGBB；尚未发送。";
        return null;
    }

    public async Task ApplyLightingConfigurationAsync(
        LightingZoneConfiguration configuration, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        if (configuration.Zone == LightingZone.Keyboard && IsKeyboardLightingPowerOnly)
        {
            ReportLightingFeedback(configuration.Zone, "当前键盘仅确认支持背光开关，未发送灯效、颜色或亮度配置。");
            return;
        }
        if (!configuration.Enabled)
        {
            StoreLightingZoneConfiguration(configuration);
            ReportLightingFeedback(configuration.Zone,
                $"{LightingZoneLabel(configuration.Zone)}当前已关闭；颜色 {configuration.Color.Hex} 与灯效参数已保存，打开后自动应用。");
            return;
        }
        TrackLightingLocalTarget(configuration);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await quickControlsController.ApplyLightingZoneAsync(configuration, cancellationToken);
            TraceLightingWrite("effect", configuration, started, result);
            if (result.Succeeded)
            {
                StoreLightingZoneConfiguration(configuration);
                // A value that did not read back must not be replaced by that same unreliable status.
                if (result.UnconfirmedDetail is null) CompleteLightingEditorDraft(configuration);
                TrackLightingLocalTarget(configuration);
            }
            else
                lightingLocalTargets.Remove(configuration.Zone);
            ReportLightingFeedback(configuration.Zone,
                lightingEditorDrafts.TryGetValue(configuration.Zone, out var newer) && newer != configuration
                    ? $"{LightingZoneLabel(configuration.Zone)}已保留最新选择，等待应用；上一条反馈不会覆盖编辑值。"
                    : result.Reason);
            StatusMessage = result.Reason;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            lightingLocalTargets.Remove(configuration.Zone);
            MarkLightingProblem(configuration.Zone, true);
            ReportLightingFeedback(configuration.Zone, $"应用灯效失败：{exception.Message}");
        }
    }

    private void TraceLightingWrite(string operation, LightingZoneConfiguration target, long started, ControlResult result)
    {
        TraceControl("lighting", $"write op={operation} zone={target.Zone} effect={target.Effect} enabled={target.Enabled} ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ok={result.Succeeded} unconfirmed={result.UnconfirmedDetail ?? "none"}");
        MarkLightingProblem(target.Zone, !result.Succeeded);
    }

    private void MarkLightingProblem(LightingZone zone, bool problem)
    {
        if (problem) lightingProblemZones.Add(zone);
        else lightingProblemZones.Remove(zone);
        OnPropertyChanged(nameof(IsLightingStatusProblem));
    }

    private LatestWrite LightingWriteFor(LightingZone zone)
    {
        if (!lightingWrites.TryGetValue(zone, out var write))
            lightingWrites[zone] = write = new(exception => ReportLightingFeedback(zone, $"灯光写入异常：{exception.Message}"));
        return write;
    }

    public void QueueSelectedLightingEdit(TimeSpan delay = default, bool powerChange = false)
    {
        if (IsSynchronizingLightingEditor) return;
        if (CaptureLightingEditor() is { } configuration) QueueLightingEdit(configuration, delay, powerChange);
    }

    /// <summary>Captured at the click: later editor changes or tab switches cannot alter this write.</summary>
    public void QueueLightingEdit(LightingZoneConfiguration configuration, TimeSpan delay = default, bool powerChange = false)
    {
        var zone = configuration.Zone;
        // A newer edit replaces a waiting power request, but must still carry its intent.
        if (powerChange) lightingPowerRequests.Add(zone);
        var queued = Stopwatch.GetTimestamp();
        LightingWriteFor(zone).Post(async () =>
        {
            TraceControl("lighting", $"start zone={zone} queuedMs={Stopwatch.GetElapsedTime(queued).TotalMilliseconds:0}");
            if (lightingPowerRequests.Remove(zone))
                await SetLightingZonePowerAsync(configuration, CancellationToken.None);
            else
                await ApplyLightingConfigurationAsync(configuration, CancellationToken.None);
        }, delay);
    }

    public void QueueLightingProfile(LightingProfile profile) =>
        QueueKeyboardProfileWrite(() => ApplyLightingProfileAsync(profile, CancellationToken.None));

    public void QueueOemDefaultLighting() =>
        QueueKeyboardProfileWrite(() => ApplyOemDefaultLightingAsync(CancellationToken.None));

    public void QueueImportedLightingProfile() =>
        QueueKeyboardProfileWrite(() => ApplyImportedLightingProfileAsync(CancellationToken.None));

    private void QueueKeyboardProfileWrite(Func<Task> write)
    {
        lightingPowerRequests.Remove(LightingZone.Keyboard);
        LightingWriteFor(LightingZone.Keyboard).Post(write);
    }

    public void QueueLightingSleepTimer()
    {
        lightingSleepWrite ??= new(exception => LightingStatusText = $"应用灯效休眠失败：{exception.Message}");
        lightingSleepWrite.Post(() => CanApplyLightingSleepTimer
            ? ApplyLightingSleepTimerAsync(CancellationToken.None)
            : Task.CompletedTask);
    }

    public Task WhenLightingWritesIdleAsync() => Task.WhenAll(
        lightingWrites.Values.Select(write => write.WhenIdleAsync())
            .Append(lightingSleepWrite?.WhenIdleAsync() ?? Task.CompletedTask));

    private void StoreLightingZoneConfiguration(LightingZoneConfiguration configuration)
    {
        var previous = lightingZoneStates.GetValueOrDefault(configuration.Zone);
        lightingZoneStates[configuration.Zone] = new(
            true,
            previous?.HardwareType,
            configuration,
            previous?.SupportedEffects ?? [configuration.Effect]);
        if (configuration.Zone == LightingZone.Keyboard)
            IsKeyboardLightingEnabled = configuration.Enabled;
    }

    private void RememberLightingEditorDraft()
    {
        if (IsSynchronizingLightingEditor ||
            lightingZoneStates.GetValueOrDefault(SelectedLightingZone)?.Configuration is not { } previous) return;
        var color = TryParseLightingColor(LightingColorHex, out var parsed)
            ? parsed : lightingEditorDrafts.GetValueOrDefault(SelectedLightingZone)?.Color ?? previous.Color;
        lightingEditorDrafts[SelectedLightingZone] = new(SelectedLightingZone, IsSelectedLightingZoneEnabled,
            SelectedLightingEffect, color, LightingBrightness, LightingSpeed, SelectedLightingDirection, LightingSleepSeconds);
        LightingStatusText = IsSelectedLightingZoneEnabled
            ? $"{SelectedLightingZoneLabel}已选择 {color.Hex}；等待自动应用。"
            : $"{SelectedLightingZoneLabel}已保存 {color.Hex}；打开后自动应用。";
    }

    private void ReportLightingFeedback(LightingZone zone, string message)
    {
        lightingZoneFeedback[zone] = message;
        if (SelectedLightingZone == zone) LightingStatusText = message;
    }

    private void CompleteLightingEditorDraft(LightingZoneConfiguration completed)
    {
        if (lightingEditorDrafts.GetValueOrDefault(completed.Zone) == completed)
            lightingEditorDrafts.Remove(completed.Zone);
    }

    private bool TryBuildSelectedLightingConfiguration(out LightingZoneConfiguration configuration)
    {
        if (!TryParseLightingColor(LightingColorHex, out var color))
        {
            configuration = default!;
            return false;
        }

        configuration = new(
            SelectedLightingZone,
            IsSelectedLightingZoneEnabled,
            SelectedLightingEffect,
            color,
            LightingBrightness,
            LightingSpeed,
            SelectedLightingDirection,
            LightingSleepSeconds);

        if (configuration.Enabled &&
            configuration.Zone is LightingZone.CoastLightbar or LightingZone.Logo)
        {
            var normalizedColor = UsesUserLightingColor(configuration.Effect) &&
                                  configuration.Color == new LightingRgb(0, 0, 0)
                ? new LightingRgb(0, 176, 255)
                : configuration.Color;
            configuration = configuration with
            {
                Color = normalizedColor,
                Brightness = configuration.Brightness == 0 ? 1 : configuration.Brightness
            };
            LightingColorHex = configuration.Color.Hex;
            LightingBrightness = configuration.Brightness;
        }
        else if (configuration.Enabled &&
                 configuration.Zone == LightingZone.LiquidCooler &&
                 configuration.Effect == LightingEffect.Static &&
                 configuration.Color == new LightingRgb(0, 0, 0))
        {
            configuration = configuration with { Color = new(0, 176, 255) };
            LightingColorHex = configuration.Color.Hex;
        }
        return true;
    }

    private static bool UsesUserLightingColor(LightingEffect effect) => effect is not (
        LightingEffect.Rainbow or
        LightingEffect.ColorMarquee or
        LightingEffect.Colorful);

    private void TrackLightingLocalTarget(LightingZoneConfiguration configuration) =>
        lightingLocalTargets[configuration.Zone] = new(
            configuration,
            utcNow().AddSeconds(3));

    private static bool LightingConfigurationsMatch(
        LightingZoneConfiguration? actual,
        LightingZoneConfiguration expected)
    {
        if (actual is null || actual.Enabled != expected.Enabled) return false;
        if (!expected.Enabled || expected.Effect == LightingEffect.OemDefault) return true;
        if (actual.Effect != expected.Effect) return false;

        if (expected.Zone == LightingZone.LiquidCooler)
            return expected.Effect != LightingEffect.Static || actual.Color == expected.Color;

        if (actual.Brightness != expected.Brightness || actual.Speed != expected.Speed) return false;
        var expectedDirection = expected.Zone == LightingZone.Keyboard && expected.Effect == LightingEffect.Wave
            ? expected.Direction
            : LightingDirection.None;
        if (actual.Direction != expectedDirection) return false;
        return !UsesUserLightingColor(expected.Effect) || actual.Color == expected.Color;
    }

    public async Task ApplyLightingSleepTimerAsync(CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        try
        {
            var result = await quickControlsController.SetLightingSleepTimerAsync(
                LightingSleepSeconds, cancellationToken);
            LightingStatusText = result.Succeeded
                ? $"灯效休眠：{result.Reason}"
                : result.Reason;
            StatusMessage = LightingStatusText;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LightingStatusText = $"应用灯效休眠失败：{exception.Message}";
        }
    }

    public async Task ApplyLightingProfileAsync(LightingProfile profile, CancellationToken cancellationToken)
    {
        if (quickControlsController is null) return;
        if (!LightingProfileCatalog.Validate(profile, out var validationReason))
        {
            LightingStatusText = validationReason;
            return;
        }

        var applied = 0;
        var skipped = 0;
        var lastReason = string.Empty;
        LightingZoneConfiguration? pendingProfileConfiguration = null;
        try
        {
            foreach (var configuration in profile.Zones)
            {
                if (!IsLightingZoneAvailable(configuration.Zone) ||
                    configuration.Zone == LightingZone.Keyboard && IsKeyboardLightingPowerOnly)
                {
                    skipped++;
                    continue;
                }
                var capability = lightingZoneStates[configuration.Zone];
                if (configuration.Effect != LightingEffect.OemDefault &&
                    !capability.SupportedEffects.Contains(configuration.Effect))
                {
                    skipped++;
                    continue;
                }
                pendingProfileConfiguration = configuration;
                lightingEditorDrafts[configuration.Zone] = configuration;
                if (SelectedLightingZone == configuration.Zone) LoadLightingEditor(configuration.Zone);
                TrackLightingLocalTarget(configuration);
                var result = await quickControlsController.ApplyLightingZoneAsync(configuration, cancellationToken);
                lastReason = result.Reason;
                if (!result.Succeeded)
                {
                    lightingLocalTargets.Remove(configuration.Zone);
                    if (lightingEditorDrafts.GetValueOrDefault(configuration.Zone) == configuration)
                        lightingEditorDrafts.Remove(configuration.Zone);
                    skipped++;
                    pendingProfileConfiguration = null;
                    continue;
                }
                applied++;
                lightingZoneStates[configuration.Zone] = capability with { Configuration = configuration };
                if (result.UnconfirmedDetail is null) CompleteLightingEditorDraft(configuration);
                TrackLightingLocalTarget(configuration);
                pendingProfileConfiguration = null;
            }
            importedLightingProfile = profile;
            LightingStatusText = applied > 0
                ? $"{profile.Name}键盘方案已应用并回读。"
                : string.IsNullOrWhiteSpace(lastReason) ? $"{profile.Name}没有可应用的已发现分区。" : lastReason;
            StatusMessage = LightingStatusText;
            LoadLightingEditor(SelectedLightingZone);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (pendingProfileConfiguration is { } failedConfiguration)
            {
                lightingLocalTargets.Remove(failedConfiguration.Zone);
                if (lightingEditorDrafts.GetValueOrDefault(failedConfiguration.Zone) == failedConfiguration)
                    lightingEditorDrafts.Remove(failedConfiguration.Zone);
                if (SelectedLightingZone == failedConfiguration.Zone)
                    LoadLightingEditor(failedConfiguration.Zone);
            }
            LightingStatusText = $"应用灯光方案失败：{exception.Message}";
        }
    }

    public void SaveMyLightingProfile()
    {
        try
        {
            var profile = BuildLightingDraft("my-lighting", "我的灯光方案");
            lightingProfileStore.Save(profile);
            importedLightingProfile = profile;
            LightingStatusText = "我的灯光方案已保存；可继续导出为 .revolight.json 分享。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"保存灯光方案失败：{exception.Message}";
        }
    }

    public void ImportLightingProfile(string path)
    {
        try
        {
            importedLightingProfile = lightingProfileStore.Import(path);
            var keyboard = importedLightingProfile.Zones[0];
            SelectedLightingZone = LightingZone.Keyboard;
            LoadLightingConfiguration(keyboard,
                lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.SupportedEffects ?? [keyboard.Effect]);
            LightingStatusText = $"已载入 {importedLightingProfile.Name}；确认参数后点击应用并回读。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"导入灯光方案失败：{exception.Message}";
        }
    }

    public void ExportLightingProfile(string path)
    {
        try
        {
            var profile = importedLightingProfile ?? BuildLightingDraft("my-lighting", "我的灯光方案");
            lightingProfileStore.Export(path, profile);
            LightingStatusText = $"已导出 {profile.Name}。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"导出灯光方案失败：{exception.Message}";
        }
    }

    public Task ApplyImportedLightingProfileAsync(CancellationToken cancellationToken) =>
        importedLightingProfile is null
            ? Task.CompletedTask
            : ApplyLightingProfileAsync(importedLightingProfile, cancellationToken);

    private void ApplyKeyboardLighting(KeyboardLightingState? state)
    {
        if (state is null) return;
        IsKeyboardLightingEnabled = state.Enabled;
        if (IsKeyboardLightingZoneAvailable && !IsKeyboardLightingPowerOnly) return;
        var configuration = lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration ??
            new(LightingZone.Keyboard, state.Enabled, LightingEffect.OemDefault, new(0, 0, 0),
                3, 2, LightingDirection.None, 120);
        StoreKeyboardLightingPower(configuration with { Enabled = state.Enabled }, state.Type);
        if (SelectedLightingZone == LightingZone.Keyboard) LoadLightingEditor(LightingZone.Keyboard);
    }

    private void StoreKeyboardLightingPower(LightingZoneConfiguration configuration, string? hardwareType)
    {
        // A confirmed power acknowledgement is not evidence of RGB configuration support.
        lightingZoneStates[LightingZone.Keyboard] = new(true, hardwareType,
            configuration with { Effect = LightingEffect.OemDefault }, [LightingEffect.OemDefault],
            HasDetailedConfiguration: false, ReportedFields: LightingReportedFields.Power);
        IsKeyboardLightingEnabled = configuration.Enabled;
        NotifyLightingCapabilitiesChanged();
    }

    private bool IsLightingZoneAvailable(LightingZone zone) =>
        lightingZoneStates.GetValueOrDefault(zone)?.Available == true;

    private bool IsKeyboardLightingPowerOnly =>
        lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard) is { Available: true, HasDetailedConfiguration: false } state &&
        state.SupportedEffects.All(effect => effect == LightingEffect.OemDefault);

    private void NotifyLightingCapabilitiesChanged()
    {
        OnPropertyChanged(nameof(IsKeyboardLightingZoneAvailable));
        OnPropertyChanged(nameof(IsCoastLightingZoneAvailable));
        OnPropertyChanged(nameof(IsLogoLightingZoneAvailable));
        OnPropertyChanged(nameof(IsWaterLightingZoneAvailable));
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(CanToggleSelectedLightingZonePower));
        OnPropertyChanged(nameof(CanApplyLightingSleepTimer));
    }

    private void LoadLightingEditor(LightingZone zone)
    {
        var state = lightingZoneStates.GetValueOrDefault(zone);
        if (lightingEditorDrafts.GetValueOrDefault(zone) is { } draft)
        {
            LoadLightingConfiguration(draft, state?.SupportedEffects ?? [draft.Effect]);
        }
        else if (state?.Configuration is { } configuration)
        {
            LoadLightingConfiguration(configuration, state.SupportedEffects);
        }
        else
        {
            LoadLightingConfiguration(
                new(zone, true, LightingEffect.Static, new(0, 176, 255), 3, 2, LightingDirection.Left, 120),
                state?.SupportedEffects ?? [LightingEffect.Static]);
        }
        OnPropertyChanged(nameof(CanApplyLighting));
        OnPropertyChanged(nameof(CanToggleSelectedLightingZonePower));
    }

    private void LoadLightingConfiguration(
        LightingZoneConfiguration configuration,
        IReadOnlyList<LightingEffect> supportedEffects)
    {
        IsSynchronizingLightingEditor = true;
        try
        {
            var effects = supportedEffects.Distinct().ToList();
            if (!effects.Contains(configuration.Effect)) effects.Add(configuration.Effect);
            var optionsChanged = !LightingEffectOptions.Select(option => option.Value).SequenceEqual(effects);
            if (optionsChanged)
            {
                LightingEffectOptions.Clear();
                foreach (var effect in effects) LightingEffectOptions.Add(LightingEffectOption.From(effect));
            }
            SelectedLightingEffect = configuration.Effect;
            // Resetting the options collection clears WPF's selected item even when the enum value did not change.
            if (optionsChanged) OnPropertyChanged(nameof(SelectedLightingEffect));
            IsSelectedLightingZoneEnabled = configuration.Enabled;
            LightingColorHex = configuration.Color.Hex;
            LightingBrightness = configuration.Brightness;
            LightingSpeed = configuration.Speed;
            SelectedLightingDirection = configuration.Direction;
        }
        finally
        {
            IsSynchronizingLightingEditor = false;
        }
    }

    private LightingProfile BuildLightingDraft(string id, string name)
    {
        var keyboard = lightingEditorDrafts.GetValueOrDefault(LightingZone.Keyboard) ??
                       lightingZoneStates.GetValueOrDefault(LightingZone.Keyboard)?.Configuration ??
                       new LightingZoneConfiguration(
                           LightingZone.Keyboard, true, LightingEffect.Static, new(0, 176, 255),
                            3, 2, LightingDirection.Left, LightingSleepSeconds);
        if (SelectedLightingZone == LightingZone.Keyboard)
        {
            if (!TryParseLightingColor(LightingColorHex, out var editorColor)) editorColor = new(0, 176, 255);
            keyboard = new(
                LightingZone.Keyboard,
                IsSelectedLightingZoneEnabled,
                SelectedLightingEffect,
                editorColor,
                LightingBrightness,
                LightingSpeed,
                SelectedLightingDirection,
                LightingSleepSeconds);
        }
        return new(LightingProfileCatalog.CurrentSchemaVersion, id, name, [keyboard]);
    }

    private void RestoreSavedLightingProfile()
    {
        try
        {
            var profile = lightingProfileStore.Load();
            if (profile is null) return;
            if (!LightingProfileCatalog.Validate(profile, out var reason))
            {
                LightingStatusText = $"已保存的灯光方案无效：{reason}";
                return;
            }

            importedLightingProfile = profile;
            LightingStatusText = $"已恢复 {profile.Name}；打开灯光页后可显式应用。";
        }
        catch (Exception exception)
        {
            LightingStatusText = $"恢复已保存灯光方案失败：{exception.Message}";
        }
    }

    private static bool TryParseLightingColor(string? text, out LightingRgb color)
    {
        color = new(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var value = text.Trim().TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb)) return false;
        color = new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static string LightingZoneLabel(LightingZone zone) => zone switch
    {
        LightingZone.Keyboard => "键盘背光",
        LightingZone.CoastLightbar => "海岸灯带",
        LightingZone.Logo => "LOGO 灯",
        LightingZone.LiquidCooler => "水冷灯",
        _ => zone.ToString()
    };
}
