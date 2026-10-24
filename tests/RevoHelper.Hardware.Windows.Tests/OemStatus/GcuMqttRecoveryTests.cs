using System.Reflection;
using MQTTnet;
using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class GcuMqttRecoveryTests
{
    [Fact]
    public async Task MainAndBootWaterUseDistinctMatchedLocalBrokerIdentities()
    {
        var main = await CaptureSessionOptionsAsync(GcuFrontendRole.Main);
        var bootWater = await CaptureSessionOptionsAsync(GcuFrontendRole.BootWater);

        Assert.Equal("UWPClient_6", main.ClientId);
        Assert.Equal("UWPClient_7", bootWater.ClientId);
        AssertSlot(main, "_6");
        AssertSlot(bootWater, "_7");
        Assert.NotEqual(main.ClientId, bootWater.ClientId);
        // Credential comparisons use booleans so failed tests cannot print authentication values.
        var mainCredentials = Assert.IsAssignableFrom<IMqttClientCredentialsProvider>(main.Credentials);
        var bootCredentials = Assert.IsAssignableFrom<IMqttClientCredentialsProvider>(bootWater.Credentials);
        var mainUser = mainCredentials.GetUserName(main);
        var bootUser = bootCredentials.GetUserName(bootWater);
        var mainPassword = mainCredentials.GetPassword(main);
        var bootPassword = bootCredentials.GetPassword(bootWater);
        Assert.False(mainUser == bootUser);
        Assert.False(mainPassword.SequenceEqual(bootPassword));
        Assert.True(mainUser[..^2] == bootUser[..^2]);
        Assert.True(mainPassword.AsSpan(0, mainPassword.Length - 2)
            .SequenceEqual(bootPassword.AsSpan(0, bootPassword.Length - 2)));
    }

    [Fact]
    public async Task RejectedBootWaterConnectionsNeverFallBackToMainIdentity()
    {
        var client = DispatchProxy.Create<IMqttClient, ClientProxy>();
        var fake = (ClientProxy)(object)client;
        fake.RejectConnections = true;
        await using var source = new GcuMqttStatusSource(client,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100), GcuFrontendRole.BootWater);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var rejectedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = source.RunAsync(_ => ValueTask.CompletedTask, _ =>
        {
            if (fake.Connects >= 2) rejectedTwice.TrySetResult();
            return ValueTask.CompletedTask;
        }, stop.Token);
        try
        {
            await rejectedTwice.Task.WaitAsync(stop.Token);
        }
        finally
        {
            stop.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }

        Assert.True(fake.ConnectionOptions.Count >= 2);
        Assert.All(fake.ConnectionOptions, options =>
        {
            Assert.Equal("UWPClient_7", options.ClientId);
            AssertSlot(options, "_7");
        });
        Assert.Equal(0, fake.Publishes);
    }

    [Fact]
    public void UnknownFrontendRoleIsRejectedBeforeAnyConnection()
    {
        var client = DispatchProxy.Create<IMqttClient, ClientProxy>();
        var fake = (ClientProxy)(object)client;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GcuMqttStatusSource(client, frontendRole: (GcuFrontendRole)int.MaxValue));
        Assert.Equal(0, fake.Connects);
    }

    private static async Task<MqttClientOptions> CaptureSessionOptionsAsync(GcuFrontendRole role)
    {
        var client = DispatchProxy.Create<IMqttClient, ClientProxy>();
        var fake = (ClientProxy)(object)client;
        await using var source = new GcuMqttStatusSource(client,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100), role);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = source.RunAsync(update =>
        {
            if (update is OemControlSessionUpdate) ready.TrySetResult();
            return ValueTask.CompletedTask;
        }, _ => ValueTask.CompletedTask, stop.Token);
        try
        {
            await ready.Task.WaitAsync(stop.Token);
            return Assert.Single(fake.ConnectionOptions);
        }
        finally
        {
            stop.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    private static void AssertSlot(MqttClientOptions options, string suffix)
    {
        Assert.True(HasAccountSuffix(options.ClientId, suffix));
        var credentials = Assert.IsAssignableFrom<IMqttClientCredentialsProvider>(options.Credentials);
        Assert.True(HasAccountSuffix(credentials.GetUserName(options), suffix));
        Assert.True(HasAccountSuffix(System.Text.Encoding.UTF8.GetString(credentials.GetPassword(options)), suffix));
        var tcp = Assert.IsType<MqttClientTcpOptions>(options.ChannelOptions);
        var (host, port) = tcp.RemoteEndpoint switch
        {
            System.Net.DnsEndPoint dns => (dns.Host, dns.Port),
            System.Net.IPEndPoint ip => (ip.Address.ToString(), ip.Port),
            _ => throw new InvalidOperationException("Expected a fixed local broker endpoint.")
        };
        Assert.Equal("127.0.0.1", host);
        Assert.Equal(13688, port);
    }

    // Keep assertion failure output from including complete local authentication material.
    private static bool HasAccountSuffix(string value, string suffix) => value.EndsWith(suffix, StringComparison.Ordinal);

    [Fact]
    public async Task OfficialFrontendDoesNotPreventIndependentSession()
    {
        var client = DispatchProxy.Create<IMqttClient, ClientProxy>();
        var fake = (ClientProxy)(object)client;
        await using var source = new GcuMqttStatusSource(client,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = source.RunAsync(update =>
        {
            if (update is OemControlSessionUpdate) ready.TrySetResult();
            return ValueTask.CompletedTask;
        }, _ => ValueTask.CompletedTask, stop.Token);
        try
        {
            await ready.Task.WaitAsync(stop.Token);
            Assert.Equal(1, fake.Connects);
            Assert.Equal("UWPClient_6", fake.LastClientId);
            Assert.NotEqual("UWPClient_5", fake.LastClientId);
            Assert.Contains("{\"Action\":\"GETSTATUS\"}", fake.Payloads);
        }
        finally
        {
            stop.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task FailedSubscriptionDisconnectsBeforeRetryAndRecovers()
    {
        var client = DispatchProxy.Create<IMqttClient, ClientProxy>();
        var fake = (ClientProxy)(object)client;
        fake.FailFirstSubscription = true;
        await using var source = new GcuMqttStatusSource(client,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(100));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = source.RunAsync(update =>
        {
            if (update is OemControlSessionUpdate) ready.TrySetResult();
            return ValueTask.CompletedTask;
        }, _ => ValueTask.CompletedTask, stop.Token);
        try
        {
            await ready.Task.WaitAsync(stop.Token);
            Assert.Equal(2, fake.Connects);
            Assert.True(fake.Disconnects >= 1);
        }
        finally
        {
            stop.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
        Assert.False(fake.Connected);
    }

    [Fact]
    public async Task StalledPublishTimesOutWithoutReplayingWrite()
    {
        var client = DispatchProxy.Create<IMqttClient, ClientProxy>();
        var fake = (ClientProxy)(object)client;
        fake.Connected = true;
        fake.StallPublish = true;
        await using var source = new GcuMqttStatusSource(client,
            operationTimeout: TimeSpan.FromMilliseconds(50));
        var write = source.WriteSettingAsync("test-command", CancellationToken.None).AsTask();
        await Assert.ThrowsAsync<TimeoutException>(() => write.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.True(write.IsCompleted); // Not just the test's outer timeout.
        Assert.Equal(1, fake.Publishes);
    }

    public class ClientProxy : DispatchProxy
    {
        public bool Connected;
        public bool FailFirstSubscription;
        public bool StallPublish;
        public bool RejectConnections;
        public int Connects;
        public int Disconnects;
        public int Publishes;
        public string? LastClientId;
        public List<MqttClientOptions> ConnectionOptions = [];
        public List<string> Payloads = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "get_IsConnected": return Connected;
                case "ConnectAsync":
                    Connects++;
                    var options = (MqttClientOptions)args![0]!;
                    ConnectionOptions.Add(options);
                    LastClientId = options.ClientId;
                    if (RejectConnections) throw new InvalidOperationException("Connection rejected for test.");
                    if (Connected) throw new InvalidOperationException("Already connected");
                    Connected = true;
                    break;
                case "SubscribeAsync":
                    if (FailFirstSubscription) { FailFirstSubscription = false; throw new IOException("Subscribe failed"); }
                    break;
                case "DisconnectAsync": Connected = false; Disconnects++; break;
                case "PublishAsync":
                    Publishes++;
                    Payloads.Add(((MqttApplicationMessage)args![0]!).ConvertPayloadToString());
                    if (StallPublish)
                    {
                        var resultType = method.ReturnType.GenericTypeArguments[0];
                        var completion = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(resultType))!;
                        return completion.GetType().GetProperty("Task")!.GetValue(completion);
                    }
                    break;
            }
            if (method.ReturnType == typeof(void)) return null;
            if (method.ReturnType == typeof(Task)) return Task.CompletedTask;
            if (method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = method.ReturnType.GenericTypeArguments[0];
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(resultType)
                    .Invoke(null, new object?[] { resultType.IsValueType ? Activator.CreateInstance(resultType) : null });
            }
            throw new NotSupportedException(method.Name);
        }
    }
}
