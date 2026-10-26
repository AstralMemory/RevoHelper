using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using RevoHelper.Core.Control;
using RevoHelper.Core.Fans;
using RevoHelper.Core.Hardware;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.OemStatus;

/// <summary>
/// Owns the authenticated local GCU MQTT session and exposes only typed,
/// allow-listed control channels plus their status readback requests.
/// </summary>
public sealed class GcuMqttStatusSource : IOemStatusSource, IGcuCpuPolicyChannel, IGcuPlatformModeChannel,
    IGcuGpuOutputModeChannel, IGcuQuickControlsChannel, IGcuCustomPerformanceChannel, IGcuFanControlChannel,
    IGcuFanBoostChannel
{
    private const string Host = "127.0.0.1";
    private const int Port = 13688;
    // The broker authenticates the complete numbered identity. Slot 5 belongs
    // to CCUWinUI and slot 10 to its tray; slot 6 was verified alongside both.
    private const string ClientId = "UWPClient_6";
    private const string UserName = "UWPClient_User_6";
    private const string Password = "UWPClient_Pwd888881772688_6";
    private const string FanReadRequest = "{\"Action\":\"GET_FAN_SPEED_CURVE_SETTING\"}";

    private readonly IMqttClient client;
    private readonly string sessionClientId;
    private readonly string sessionUserName;
    private readonly string sessionPassword;
    private readonly TimeSpan reconnectDelay;
    private readonly TimeSpan operationTimeout;
    private TaskCompletionSource reconnectRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool reconnectPending;
    private readonly SemaphoreSlim cpuStatusRequestLock = new(1, 1);
    private readonly object cpuStatusSync = new();
    private TaskCompletionSource<GcuCpuPolicyStatus>? pendingCpuStatus;
    private GcuCpuPolicyStatus? cachedCpuStatus;
    private OemRuntimeEvidence? publishedRuntimeEvidence;
    private readonly SemaphoreSlim gpuStatusRequestLock = new(1, 1);
    private readonly object gpuStatusSync = new();
    private TaskCompletionSource<GcuGpuOutputModeStatus>? pendingGpuStatus;
    private readonly StatusRequest settingStatusRequest = new();
    private readonly StatusRequest deviceSwitchStatusRequest = new();
    private readonly StatusRequest keyboardStatusRequest = new();
    private readonly StatusRequest lightbarStatusRequest = new();
    private readonly StatusRequest logoStatusRequest = new();
    private readonly StatusRequest batteryStatusRequest = new();
    private readonly StatusRequest liquidCoolingStatusRequest = new();
    private readonly StatusRequest customPerformanceStatusRequest = new();
    private readonly StatusRequest fanTableStatusRequest = new();
    private readonly StatusRequest fanResponseStatusRequest = new();
    private bool disposed;

    public GcuMqttStatusSource(IMqttClient? mqttClient = null,
        TimeSpan? reconnectDelay = null, TimeSpan? operationTimeout = null,
        GcuFrontendRole frontendRole = GcuFrontendRole.Main)
    {
        (sessionClientId, sessionUserName, sessionPassword) = frontendRole switch
        {
            GcuFrontendRole.Main => (ClientId, UserName, Password),
            GcuFrontendRole.BootWater => (ForBootWater(ClientId), ForBootWater(UserName), ForBootWater(Password)),
            _ => throw new ArgumentOutOfRangeException(nameof(frontendRole), "Unsupported local GCU frontend role.")
        };
        client = mqttClient ?? new MqttClientFactory().CreateMqttClient();
        this.reconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(3);
        this.operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(3);
    }

    private static string ForBootWater(string mainIdentityPart)
    {
        // OEM authentication binds all three identity fields to the same numbered slot.
        // This dedicated helper slot must never fall back to the main application's slot.
        const string mainSuffix = "_6";
        if (!mainIdentityPart.EndsWith(mainSuffix, StringComparison.Ordinal))
            throw new InvalidOperationException("The main GCU identity does not have the expected local slot suffix.");
        return mainIdentityPart[..^mainSuffix.Length] + "_7";
    }

    public async Task RunAsync(
        Func<OemStatusUpdate, ValueTask> onUpdate,
        Func<Exception, ValueTask> onError,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        ArgumentNullException.ThrowIfNull(onError);

        Func<MqttApplicationMessageReceivedEventArgs, Task> onMessage = async args =>
        {
            try
            {
                var payload = args.ApplicationMessage.ConvertPayloadToString();
                OemStatusUpdate? update;
                if (string.Equals(args.ApplicationMessage.Topic, "Fan/Status", StringComparison.Ordinal))
                {
                    CompletePending(customPerformanceStatusRequest, payload);
                    CompletePending(fanResponseStatusRequest, payload);
                    var cpuStatus = GcuCpuPolicyStatusParser.Parse(payload);
                    if (cpuStatus is not null)
                    {
                        GcuCpuPolicyStatus mergedStatus;
                        TaskCompletionSource<GcuCpuPolicyStatus>? waiter;
                        lock (cpuStatusSync)
                        {
                            mergedStatus = GcuCpuPolicyStatusAccumulator.Merge(cachedCpuStatus, cpuStatus);
                            cachedCpuStatus = mergedStatus;
                            waiter = pendingCpuStatus;
                            pendingCpuStatus = null;
                        }

                        waiter?.TrySetResult(mergedStatus);
                        var evidence = GcuRuntimeCapabilityMapper.Map(mergedStatus);
                        if (!SameEvidence(publishedRuntimeEvidence, evidence))
                        {
                            publishedRuntimeEvidence = evidence;
                            await onUpdate(new OemRuntimeCapabilitiesUpdate(evidence)).ConfigureAwait(false);
                        }
                        update = GcuPlatformModeStatusMapper.Map(mergedStatus) is { } selection
                            ? new OemPlatformModeUpdate(selection)
                            : null;
                    }
                    else update = null;
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "Setting/Status", StringComparison.Ordinal))
                {
                    var gpuStatus = GcuGpuOutputModeStatusParser.Parse(payload);
                    if (gpuStatus is not null)
                    {
                        TaskCompletionSource<GcuGpuOutputModeStatus>? waiter;
                        lock (gpuStatusSync)
                        {
                            waiter = pendingGpuStatus;
                            pendingGpuStatus = null;
                        }
                        waiter?.TrySetResult(gpuStatus);
                    }
                    CompletePending(settingStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "Settings/DeviceSwitchItemStatus", StringComparison.Ordinal))
                {
                    CompletePending(deviceSwitchStatusRequest, payload);
                    update = null;
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "Keyboard/Status", StringComparison.Ordinal))
                {
                    CompletePending(keyboardStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "HidLightbar/Status", StringComparison.Ordinal))
                {
                    CompletePending(lightbarStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "HidLightbar_Logo/Status", StringComparison.Ordinal))
                {
                    CompletePending(logoStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "System/BatteryProtection", StringComparison.Ordinal))
                {
                    CompletePending(batteryStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "BT_LC/Status", StringComparison.Ordinal))
                {
                    CompletePending(liquidCoolingStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else if (string.Equals(args.ApplicationMessage.Topic, "Fan/Table", StringComparison.Ordinal))
                {
                    CompletePending(fanTableStatusRequest, payload);
                    update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                }
                else update = OemStatusMessageRouter.Route(args.ApplicationMessage.Topic, payload);
                if (update is not null)
                {
                    await onUpdate(update).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                // A malformed optional status must not tear down all control channels.
                await onError(exception).ConfigureAwait(false);
            }
        };
        client.ApplicationMessageReceivedAsync += onMessage;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var disconnected = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                Func<MqttClientDisconnectedEventArgs, Task> onDisconnected = _ =>
                {
                    disconnected.TrySetResult();
                    return Task.CompletedTask;
                };
                client.DisconnectedAsync += onDisconnected;
                try
                {
                    reconnectRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    reconnectPending = false;
                    await onUpdate(new OemConnectionStateUpdate("connecting", "正在连接 GCU 控制通道…"));
                    lock (cpuStatusSync) cachedCpuStatus = null;
                    publishedRuntimeEvidence = null;
                    var options = new MqttClientOptionsBuilder()
                        .WithClientId(sessionClientId)
                        .WithTcpServer(Host, Port)
                        .WithCredentials(sessionUserName, sessionPassword)
                        .WithProtocolVersion(MqttProtocolVersion.V311)
                        .WithCleanSession(true)
                        .WithKeepAlivePeriod(TimeSpan.FromSeconds(1))
                        .WithTimeout(TimeSpan.FromSeconds(3))
                        .Build();

                    await ExecuteTransportAsync(token => client.ConnectAsync(options, token), cancellationToken).ConfigureAwait(false);
                    await ExecuteTransportAsync(token => client.SubscribeAsync(
                        new MqttClientSubscribeOptionsBuilder()
                            .WithTopicFilter("System/CpuInfo", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("Fan/Table", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("Fan/Status", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("Setting/Status", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("Settings/DeviceSwitchItemStatus", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("Keyboard/Status", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("HidLightbar/Status", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("HidLightbar_Logo/Status", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("System/BatteryProtection", MqttQualityOfServiceLevel.AtMostOnce)
                            .WithTopicFilter("BT_LC/Status", MqttQualityOfServiceLevel.AtMostOnce)
                            .Build(),
                        token), cancellationToken).ConfigureAwait(false);

                    // Request the live mode too: receiving a curve must not leave
                    // the homepage on a saved/default mode until its next edit.
                    await PublishFanControlAsync("{\"Action\":\"GETSTATUS\"}", cancellationToken).ConfigureAwait(false);
                    await RequestFanTableAsync(cancellationToken).ConfigureAwait(false);
                    await onUpdate(new OemConnectionStateUpdate("connected", "GCU 控制通道已连接。"));
                    await onUpdate(new OemControlSessionUpdate()).ConfigureAwait(false);
                    await Task.WhenAny(disconnected.Task, reconnectRequested.Task)
                        .WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    await onError(exception).ConfigureAwait(false);
                }
                finally
                {
                    reconnectPending = true;
                    // Connect may succeed before Subscribe or initialization fails. Always close that
                    // session, including cancellation, before the next Connect attempt.
                    try { await DisconnectTransportAsync().ConfigureAwait(false); }
                    catch (Exception exception) { await onError(exception).ConfigureAwait(false); }
                    client.DisconnectedAsync -= onDisconnected;
                }

                try
                {
                    await onUpdate(new OemConnectionStateUpdate("reconnecting", "GCU 控制通道已断开，正在等待重连；未重发先前操作。"));
                    await Task.Delay(reconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally { client.ApplicationMessageReceivedAsync -= onMessage; }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lock (cpuStatusSync)
        {
            pendingCpuStatus?.TrySetException(new ObjectDisposedException(nameof(GcuMqttStatusSource)));
            pendingCpuStatus = null;
        }
        lock (gpuStatusSync)
        {
            pendingGpuStatus?.TrySetException(new ObjectDisposedException(nameof(GcuMqttStatusSource)));
            pendingGpuStatus = null;
        }
        CancelPending(settingStatusRequest);
        CancelPending(deviceSwitchStatusRequest);
        CancelPending(keyboardStatusRequest);
        CancelPending(lightbarStatusRequest);
        CancelPending(logoStatusRequest);
        CancelPending(batteryStatusRequest);
        CancelPending(liquidCoolingStatusRequest);
        CancelPending(customPerformanceStatusRequest);
        CancelPending(fanTableStatusRequest);
        CancelPending(fanResponseStatusRequest);

        if (client.IsConnected)
        {
            await DisconnectTransportAsync().ConfigureAwait(false);
        }

        client.Dispose();
    }

    private static bool SameEvidence(OemRuntimeEvidence? previous, OemRuntimeEvidence current) =>
        previous is not null &&
        previous.SupportedPlatformModes.SetEquals(current.SupportedPlatformModes) &&
        previous.HasCpuPowerLimits == current.HasCpuPowerLimits &&
        previous.HasFanCurves == current.HasFanCurves &&
        previous.HasGpuOutputModes == current.HasGpuOutputModes &&
        previous.HasLighting == current.HasLighting &&
        previous.HasLiquidCooling == current.HasLiquidCooling;

    public async ValueTask<GcuCpuPolicyStatus> ReadStatusAsync(CancellationToken cancellationToken)
    {
        await cpuStatusRequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureControlSessionAvailable();
            var waiter = new TaskCompletionSource<GcuCpuPolicyStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (cpuStatusSync)
            {
                pendingCpuStatus = waiter;
            }

            try
            {
                await PublishFanControlAsync(GcuCpuPolicyCommand.GetStatus(), cancellationToken).ConfigureAwait(false);
                return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // CPU status is the control-session health/readback channel. A silent backend
                // must not remain permanently "connected" just because MQTT still answers pings.
                reconnectPending = true;
                reconnectRequested.TrySetResult();
                throw new TimeoutException("GCU 性能状态回读超时，正在重连；本次操作未确认，未自动重发。");
            }
            finally
            {
                lock (cpuStatusSync)
                {
                    if (ReferenceEquals(pendingCpuStatus, waiter))
                    {
                        pendingCpuStatus = null;
                    }
                }
            }
        }
        finally
        {
            cpuStatusRequestLock.Release();
        }
    }

    public ValueTask WritePl1Async(int watts, CancellationToken cancellationToken) =>
        WritePowerLimitAsync(isPl1: true, watts, cancellationToken);

    public ValueTask WritePl2Async(int watts, CancellationToken cancellationToken) =>
        WritePowerLimitAsync(isPl1: false, watts, cancellationToken);

    async ValueTask<GcuGpuOutputModeStatus> IGcuGpuOutputModeChannel.ReadStatusAsync(
        CancellationToken cancellationToken)
    {
        await gpuStatusRequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureControlSessionAvailable();
            var waiter = new TaskCompletionSource<GcuGpuOutputModeStatus>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            lock (gpuStatusSync) pendingGpuStatus = waiter;
            try
            {
                await PublishSettingControlAsync(GcuGpuOutputModeCommand.GetStatus, cancellationToken)
                    .ConfigureAwait(false);
                return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (gpuStatusSync)
                {
                    if (ReferenceEquals(pendingGpuStatus, waiter)) pendingGpuStatus = null;
                }
            }
        }
        finally
        {
            gpuStatusRequestLock.Release();
        }
    }

    async ValueTask IGcuGpuOutputModeChannel.WriteModeAsync(
        GpuOutputMode mode,
        CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishSettingControlAsync(GcuGpuOutputModeCommand.Select(mode), cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<string> ReadSettingStatusAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(settingStatusRequest,
            () => PublishSettingControlAsync(GcuQuickControlsCommand.GetStatus, cancellationToken), cancellationToken);

    public ValueTask<string> ReadDeviceSwitchStatusAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(deviceSwitchStatusRequest,
            () => PublishSettingControlAsync(GcuQuickControlsCommand.GetStatus, cancellationToken), cancellationToken);

    public ValueTask<string> ReadKeyboardStatusAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(keyboardStatusRequest,
            () => PublishKeyboardControlAsync(GcuQuickControlsCommand.GetStatus, cancellationToken), cancellationToken);

    public ValueTask<string> ReadLightingStatusAsync(LightingZone zone, CancellationToken cancellationToken) => zone switch
    {
        LightingZone.Keyboard => ReadKeyboardStatusAsync(cancellationToken),
        LightingZone.CoastLightbar => ReadStatusAsync(lightbarStatusRequest,
            () => PublishAsync("HidLightbar/Ctrl", GcuQuickControlsCommand.GetStatus, cancellationToken), cancellationToken),
        LightingZone.Logo => ReadStatusAsync(logoStatusRequest,
            () => PublishAsync("HidLightbar_Logo/Ctrl", GcuQuickControlsCommand.GetStatus, cancellationToken), cancellationToken),
        LightingZone.LiquidCooler => ReadLiquidCoolingAsync(cancellationToken),
        _ => throw new ArgumentOutOfRangeException(nameof(zone))
    };

    public ValueTask<string> ReadBatteryProtectionAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(batteryStatusRequest,
            () => PublishBatteryProtectionControlAsync(GcuQuickControlsCommand.GetBatteryProtection, cancellationToken), cancellationToken);

    public ValueTask<string> ReadLiquidCoolingAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(liquidCoolingStatusRequest,
            () => PublishLiquidCoolingControlAsync(GcuQuickControlsCommand.GetStatus, cancellationToken), cancellationToken);

    public async ValueTask<int?> ReadBatteryChargeLimitAsync(CancellationToken cancellationToken) =>
        (await ReadStatusAsync(cancellationToken).ConfigureAwait(false)).BatteryChargingLimitPercent;

    public async ValueTask WriteSettingAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishSettingControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteKeyboardAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishKeyboardControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteLightingAsync(
        LightingZone zone,
        string payload,
        CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        var topic = zone switch
        {
            LightingZone.Keyboard => "Keyboard/Ctrl",
            LightingZone.CoastLightbar => "HidLightbar/Ctrl",
            LightingZone.Logo => "HidLightbar_Logo/Ctrl",
            LightingZone.LiquidCooler => "BT_LC/Control",
            _ => throw new ArgumentOutOfRangeException(nameof(zone))
        };
        await PublishAsync(topic, payload, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<string> WriteLightingAndReadStatusAsync(
        LightingZone zone,
        string payload,
        CancellationToken cancellationToken) => zone switch
        {
            LightingZone.Keyboard => WriteAndReadStatusAsync(
                zone,
                keyboardStatusRequest,
                () => PublishAsync("Keyboard/Ctrl", payload, cancellationToken),
                () => PublishAsync("Keyboard/Ctrl", GcuQuickControlsCommand.GetStatus, cancellationToken),
                cancellationToken),
            LightingZone.CoastLightbar => WriteAndReadStatusAsync(
                zone,
                lightbarStatusRequest,
                () => PublishAsync("HidLightbar/Ctrl", payload, cancellationToken),
                () => PublishAsync("HidLightbar/Ctrl", GcuQuickControlsCommand.GetStatus, cancellationToken),
                cancellationToken),
            LightingZone.Logo => WriteAndReadStatusAsync(
                zone,
                logoStatusRequest,
                () => PublishAsync("HidLightbar_Logo/Ctrl", payload, cancellationToken),
                () => PublishAsync("HidLightbar_Logo/Ctrl", GcuQuickControlsCommand.GetStatus, cancellationToken),
                cancellationToken),
            LightingZone.LiquidCooler => WriteAndReadStatusAsync(
                zone,
                liquidCoolingStatusRequest,
                () => PublishAsync("BT_LC/Control", payload, cancellationToken),
                () => PublishAsync("BT_LC/Control", GcuQuickControlsCommand.GetStatus, cancellationToken),
                cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(zone))
        };

    public async ValueTask WriteBatteryProtectionAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishBatteryProtectionControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteLiquidCoolingAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishLiquidCoolingControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteBatteryChargeLimitAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<string> ReadCustomPerformanceStatusAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(customPerformanceStatusRequest,
            () => PublishFanControlAsync(GcuCpuPolicyCommand.GetStatus(), cancellationToken), cancellationToken);

    public async ValueTask WriteCustomPerformanceAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<FanCurveTable?> ReadFanTableAsync(CancellationToken cancellationToken)
    {
        var payload = await ReadStatusAsync(
            fanTableStatusRequest,
            () => RequestFanTableAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return OemFanTableParser.Parse(payload);
    }

    public async ValueTask<FanResponseSettings?> ReadFanResponseAsync(CancellationToken cancellationToken)
    {
        var payload = await ReadStatusAsync(
            fanResponseStatusRequest,
            () => PublishFanControlAsync(GcuCpuPolicyCommand.GetStatus(), cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var response = GcuFanResponseStatusParser.Parse(payload);
        if (response is null) return null;
        var table = await ReadFanTableAsync(cancellationToken).ConfigureAwait(false);
        return table is null
            ? response
            : response with { IndependentCpuGpuControl = table.IndependentCpuGpuControl };
    }

    public async ValueTask WriteFanControlAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<GcuCpuPolicyStatus> ReadFanBoostStatusAsync(CancellationToken cancellationToken) =>
        ReadStatusAsync(cancellationToken);

    public async ValueTask WriteFanBoostAsync(string payload, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask WriteModeAsync(
        RevoHelper.Core.Control.PlatformModeSelection selection,
        CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(GcuPlatformModeCommand.SelectMode(selection), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask WriteTurboStrategyAsync(
        RevoHelper.Core.Control.PlatformMode mode,
        CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(GcuPlatformModeCommand.SelectTurboStrategy(mode), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask WriteCompanionModeAsync(
        RevoHelper.Core.Control.PlatformMode mode,
        CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        var message = new MqttApplicationMessageBuilder()
            .WithTopic("LCHWOC/Control")
            .WithPayload(GcuPlatformModeCommand.CompanionMode(mode))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build();
        await ExecuteTransportAsync(token => client.PublishAsync(message, token), cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask WritePowerLimitAsync(bool isPl1, int watts, CancellationToken cancellationToken)
    {
        EnsureControlSessionAvailable();
        await PublishFanControlAsync(GcuCpuPolicyCommand.SetPowerLimit(isPl1, watts), cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureControlSessionAvailable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!client.IsConnected || reconnectPending)
        {
            throw new InvalidOperationException("GCU 控制通道尚未连接，请稍后重试。");
        }
    }

    private Task PublishFanControlAsync(string payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic("Fan/Control")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build();
        return ExecuteTransportAsync(token => client.PublishAsync(message, token), cancellationToken);
    }

    private Task PublishSettingControlAsync(string payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic("Setting/Control")
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build();
        return ExecuteTransportAsync(token => client.PublishAsync(message, token), cancellationToken);
    }

    private Task PublishKeyboardControlAsync(string payload, CancellationToken cancellationToken) =>
        PublishAsync("Keyboard/Ctrl", payload, cancellationToken);

    private Task PublishBatteryProtectionControlAsync(string payload, CancellationToken cancellationToken) =>
        PublishAsync("BatteryProtection/Control", payload, cancellationToken);

    private Task PublishLiquidCoolingControlAsync(string payload, CancellationToken cancellationToken) =>
        PublishAsync("BT_LC/Control", payload, cancellationToken);

    private Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build();
        return ExecuteTransportAsync(token => client.PublishAsync(message, token), cancellationToken);
    }

    private async Task ExecuteTransportAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operationTimeout);
        try
        {
            await operation(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            reconnectPending = true;
            reconnectRequested.TrySetResult();
            throw new TimeoutException("GCU 通道响应超时，正在重新连接；本次操作未确认，未自动重发。");
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            reconnectPending = true;
            reconnectRequested.TrySetResult();
            throw;
        }
    }

    private async Task DisconnectTransportAsync()
    {
        if (!client.IsConnected) return;
        using var timeout = new CancellationTokenSource(operationTimeout);
        await client.DisconnectAsync(new MqttClientDisconnectOptions(), timeout.Token)
            .WaitAsync(timeout.Token).ConfigureAwait(false);
    }

    private async ValueTask<string> ReadStatusAsync(
        StatusRequest statusRequest,
        Func<Task> request,
        CancellationToken cancellationToken)
    {
        await statusRequest.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureControlSessionAvailable();
            var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (statusRequest.Sync) statusRequest.Pending = waiter;
            try
            {
                await request().ConfigureAwait(false);
                return await waiter.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                lock (statusRequest.Sync)
                {
                    if (ReferenceEquals(statusRequest.Pending, waiter)) statusRequest.Pending = null;
                }
            }
        }
        finally
        {
            statusRequest.Gate.Release();
        }
    }

    private async ValueTask<string> WriteAndReadStatusAsync(
        LightingZone zone,
        StatusRequest statusRequest,
        Func<Task> write,
        Func<Task> requestFallback,
        CancellationToken cancellationToken)
    {
        await statusRequest.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureControlSessionAvailable();
            var waiter = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (statusRequest.Sync) statusRequest.Pending = waiter;
            try
            {
                await write().ConfigureAwait(false);
                return await LightingStatusReadback.WaitAsync(zone, waiter.Task, requestFallback, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                lock (statusRequest.Sync)
                {
                    if (ReferenceEquals(statusRequest.Pending, waiter)) statusRequest.Pending = null;
                }
            }
        }
        finally
        {
            statusRequest.Gate.Release();
        }
    }

    private static void CompletePending(StatusRequest statusRequest, string payload)
    {
        TaskCompletionSource<string>? waiter;
        lock (statusRequest.Sync)
        {
            waiter = statusRequest.Pending;
            statusRequest.Pending = null;
        }
        waiter?.TrySetResult(payload);
    }

    private static void CancelPending(StatusRequest statusRequest)
    {
        lock (statusRequest.Sync)
        {
            statusRequest.Pending?.TrySetException(new ObjectDisposedException(nameof(GcuMqttStatusSource)));
            statusRequest.Pending = null;
        }
    }

    private sealed class StatusRequest
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public object Sync { get; } = new();
        public TaskCompletionSource<string>? Pending { get; set; }
    }

    private Task RequestFanTableAsync(CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic("Fan/Control")
            .WithPayload(FanReadRequest)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.ExactlyOnce)
            .Build();
        return ExecuteTransportAsync(token => client.PublishAsync(message, token), cancellationToken);
    }

}
