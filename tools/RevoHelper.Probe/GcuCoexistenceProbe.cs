using System.Reflection;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Formatter;
using RevoHelper.Hardware.Windows.OemControl;
using RevoHelper.Hardware.Windows.OemStatus;
using RevoHelper.Core.Control;

internal static class GcuCoexistenceProbe
{
    public static async Task<int> RunManagedTrialAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        await using var source = new GcuMqttStatusSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        var run = source.RunAsync(update =>
        {
            if (update is OemControlSessionUpdate) ready.TrySetResult();
            return ValueTask.CompletedTask;
        }, exception => { errors.Add(exception.GetType().Name); return ValueTask.CompletedTask; }, stop.Token);
        var modeController = new GcuPlatformModeController(source);
        var cpuController = new GcuCpuPowerLimitController(source);
        PlatformModeSelection? baseline = null;
        CpuPolicy? baselineCpu = null;
        PlatformModeSelection? target = null;
        var succeeded = false;
        try
        {
            await ready.Task.WaitAsync(stop.Token);
            baseline = await modeController.ReadPlatformModeAsync(stop.Token);
            baselineCpu = await cpuController.ReadCpuPolicyAsync(stop.Token);
            if (baseline is null || baselineCpu is null) throw new InvalidOperationException("Missing baseline");
            var fan = await source.ReadFanTableAsync(stop.Token);
            var fanResponse = await source.ReadFanResponseAsync(stop.Token);
            target = new(baseline.Mode == PlatformMode.Balanced ? PlatformMode.Office : PlatformMode.Balanced, 1);
            var result = await modeController.SetPlatformModeAsync(target, stop.Token);
            var after = await modeController.ReadPlatformModeAsync(stop.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), stop.Token);
            var settled = await modeController.ReadPlatformModeAsync(stop.Token);
            succeeded = result.Succeeded && after == target && settled == target && fan is not null && fanResponse is not null;
            Console.WriteLine(JsonSerializer.Serialize(new { Trial = "Independent frontend mode control", Baseline = baseline,
                Target = target, result.Succeeded, After = after, Settled = settled, FanTableAvailable = fan is not null,
                FanResponseAvailable = fanResponse is not null, ConnectionErrors = errors }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ErrorType = exception.GetType().Name }));
        }
        finally
        {
            try
            {
                if (baseline is not null)
                {
                    var current = await modeController.ReadPlatformModeAsync(CancellationToken.None);
                    if (current != baseline && current != target)
                        throw new InvalidOperationException("External mode changed; restoration not replayed");
                    if (current != baseline)
                    {
                        var restored = await modeController.SetPlatformModeAsync(baseline, CancellationToken.None);
                        if (!restored.Succeeded) throw new InvalidOperationException("Mode restoration failed");
                    }
                    var restoredCpu = await cpuController.ReadCpuPolicyAsync(CancellationToken.None);
                    if (baselineCpu is not null && restoredCpu != baselineCpu)
                    {
                        var restored = await cpuController.ApplyCpuPolicyAsync(baselineCpu, CancellationToken.None);
                        if (!restored.Succeeded) throw new InvalidOperationException("CPU restoration failed");
                    }
                    var mode = await modeController.ReadPlatformModeAsync(CancellationToken.None);
                    var cpu = await cpuController.ReadCpuPolicyAsync(CancellationToken.None);
                    var restoredExactly = mode == baseline && cpu == baselineCpu;
                    succeeded &= restoredExactly;
                    Console.WriteLine(JsonSerializer.Serialize(new { Restored = restoredExactly, Mode = mode, Cpu = cpu }));
                }
            }
            catch (Exception exception)
            {
                succeeded = false;
                Console.WriteLine(JsonSerializer.Serialize(new { RestorationError = exception.Message }));
            }
            stop.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
        return succeeded ? 0 : 5;
    }

    // Diagnostic-only, no fixed frontend ID and no setting writes.
    public static async Task<int> RunAsync(string clientId)
    {
        var account = 5;
        if (clientId.StartsWith("slot", StringComparison.Ordinal) &&
            int.TryParse(clientId[4..], out var candidate) && candidate is >= 6 and <= 9)
        {
            account = candidate;
            clientId = $"UWPClient_{account}";
        }
        else if (!clientId.Contains("RevoHelper_", StringComparison.Ordinal))
            throw new ArgumentException("A distinct RevoHelper diagnostic identity is required.");
        using var client = new MqttClientFactory().CreateMqttClient();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var sync = new object();
        var fanStatusCount = 0;
        var fanTableCount = 0;
        int? operatingMode = null;
        int? customProfile = null;
        int? fanDelay = null;
        var disconnected = false;
        client.DisconnectedAsync += _ => { disconnected = true; return Task.CompletedTask; };
        client.ApplicationMessageReceivedAsync += args =>
        {
            var text = args.ApplicationMessage.ConvertPayloadToString();
            lock (sync)
            {
                if (args.ApplicationMessage.Topic == "Fan/Status")
                {
                    fanStatusCount++;
                    var status = GcuCpuPolicyStatusParser.Parse(text);
                    operatingMode = status?.OperatingMode ?? operatingMode;
                    customProfile = status?.CustomProfileIndex ?? customProfile;
                    fanDelay = GcuFanResponseStatusParser.Parse(text)?.TransitionSensitivity ?? fanDelay;
                }
                if (args.ApplicationMessage.Topic == "Fan/Table") fanTableCount++;
            }
            return Task.CompletedTask;
        };
        try
        {
            string Credential(string field)
            {
                var value = (string)typeof(GcuMqttStatusSource)
                    .GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
                return value[..value.LastIndexOf('_')] + $"_{account}";
            }
            var options = new MqttClientOptionsBuilder()
                .WithClientId(clientId).WithTcpServer("127.0.0.1", 13688)
                .WithCredentials(Credential("UserName"), Credential("Password"))
                .WithProtocolVersion(MqttProtocolVersion.V311).WithCleanSession(true)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(2)).WithTimeout(TimeSpan.FromSeconds(3)).Build();
            var result = await client.ConnectAsync(options, stop.Token);
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter("Fan/Status").WithTopicFilter("Fan/Table").Build(), stop.Token);
            foreach (var payload in new[] { "{\"Action\":\"GETSTATUS\"}", "{\"Action\":\"GET_FAN_SPEED_CURVE_SETTING\"}" })
                await client.PublishAsync(new MqttApplicationMessageBuilder()
                    .WithTopic("Fan/Control").WithPayload(payload).Build(), stop.Token);
            await Task.Delay(TimeSpan.FromSeconds(8), stop.Token);
            object evidence;
            lock (sync) evidence = new { ReadOnly = true, clientId, Result = result.ResultCode.ToString(),
                Connected = client.IsConnected, disconnected, fanStatusCount, fanTableCount,
                operatingMode, customProfile, FanDelayProtocolValue = fanDelay };
            Console.WriteLine(JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            return client.IsConnected && fanStatusCount > 0 && fanTableCount > 0 ? 0 : 5;
        }
        catch (Exception exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { ReadOnly = true, clientId,
                Connected = client.IsConnected, ErrorType = exception.GetType().Name }));
            return 5;
        }
        finally
        {
            if (client.IsConnected)
                try { await client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None); }
                catch { }
        }
    }
}
