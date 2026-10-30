using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.Hardware.Windows.Tests.BatteryCharge;

public sealed class ReviewedChargeTransportTests
{
    [Fact]
    public void ConstructionAndUnusedDisposePerformNoReadsOrWrites()
    {
        var backend = new FakeBackend();
        using (var transport = Create(backend)) { Assert.Empty(backend.Calls); }
        Assert.Equal(["dispose"], backend.Calls);
        using var unusedRealTransport = new ReviewedChargeTransport(); // Must not touch hardware.
    }

    [Fact]
    public void ArbitraryAddressAndWriteBeforeExplicitReadPerformNoBackendCalls()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.ReadByte(0xFFFF));
        Assert.Throws<InvalidOperationException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
        Assert.Empty(backend.Calls);
    }

    [Fact]
    public void WrongSystemIdentityStopsBeforeWmiMetadataOrEcMethods()
    {
        var backend = new FakeBackend { Identity = ReviewedChargeProtocolTests.ReviewedIdentity with { EcMinor = 26 } };
        using var transport = Create(backend);
        Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.Equal(["identity"], backend.Calls);
    }

    [Fact]
    public void NotAdministratorStopsBeforeInterfaceAcquisition()
    {
        var backend = new FakeBackend { Administrator = false }; using var transport = Create(backend);
        Assert.Throws<UnauthorizedAccessException>(() => transport.ReadSnapshot());
        Assert.Equal(["identity", "administrator"], backend.Calls);
    }

    [Fact]
    public void MetadataMismatchStopsBeforeEcMethods()
    {
        var backend = new FakeBackend(); backend.Signature = backend.Signature with { MethodId = 5 };
        using var transport = Create(backend);
        Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.Equal(["identity", "administrator", "signature"], backend.Calls);
    }

    [Fact]
    public void AmbiguousOrInactiveInstancesAreNeverProbed()
    {
        var backend = new FakeBackend(); backend.Instances = backend.Instances[..9];
        using var transport = Create(backend);
        Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.Empty(backend.Packets);
    }

    [Fact]
    public void InitialSnapshotConfirmsProjectTwiceThenVersionBeforeOtherRegisters()
    {
        var backend = new FakeBackend(); var waits = new List<TimeSpan>();
        using var transport = new ReviewedChargeTransport(backend, waits.Add);
        var snapshot = transport.ReadSnapshot();
        Assert.Equal(new ushort[] { 0x0740, 0x0740, 0x0454, 0x0455, 0x047D },
            backend.Packets.Take(5).Select(packet => (ushort)packet).ToArray());
        Assert.Equal(ReviewedChargeProtocol.SnapshotAddresses.Count + 1, backend.Packets.Count);
        Assert.All(backend.Packets, packet => Assert.Equal(0x0100UL, packet >> 32));
        Assert.All(backend.InstanceNames, instance => Assert.Equal(@"ACPI\PNP0C14\1_0", instance));
        Assert.All(backend.Timeouts, timeout => Assert.Equal(TimeSpan.FromSeconds(3), timeout));
        Assert.All(waits, wait => Assert.Equal(TimeSpan.FromMilliseconds(10), wait));
        Assert.Equal(backend.Packets.Count, waits.Count);
        Assert.True(ReviewedChargeProtocol.SnapshotIdentityMatches(snapshot));
        Assert.Equal((byte)7, snapshot.Byte(0x07C3));
        Assert.Equal(75, snapshot.WindowsPercent);
        Assert.True(snapshot.PowerOnline);
        Assert.Equal(30000u, snapshot.ChargeRateMw);
    }

    [Fact]
    public void WrongProjectStopsImmediatelyAndWrongVersionStopsBeforeControlRegisters()
    {
        var backend = new FakeBackend(); backend.Registers[0x0740] = 0x1B;
        using (var transport = Create(backend))
            Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.Single(backend.Packets);
        backend = new FakeBackend(); backend.Registers[0x0454] = 0x58;
        using (var transport = Create(backend))
            Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.DoesNotContain(backend.Packets, packet => (ushort)packet == 0x07C3);
    }

    [Fact]
    public void UnstableProjectConfirmationStopsWithoutReadingVersions()
    {
        var backend = new FakeBackend(); var count = 0;
        backend.OnRead = address => address == 0x0740 && ++count == 2 ? 0x1Bu : backend.Registers[address];
        using var transport = Create(backend);
        Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.Equal(2, backend.Packets.Count);
    }

    [Theory]
    [InlineData(null)] [InlineData(0xFEFEFEFEu)]
    public void MissingOrFailureReturnNeverBecomesAByte(uint? response)
    {
        var backend = new FakeBackend { OnRead = _ => response }; using var transport = Create(backend);
        Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        Assert.Single(backend.Packets);
    }

    [Fact]
    public void ReadByteKeepsRawHighBitsButOnlyLowByteHasMeaning()
    {
        var backend = new FakeBackend(); backend.Registers[0x0742] = 0x12345622;
        using var transport = Create(backend);
        Assert.Equal(0x12345622u, transport.ReadByte(0x0742));
    }

    [Fact]
    public void OwnedPlatformWriteRechecksExpectedValueAndVerifiesTarget()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); backend.Packets.Clear();
        transport.WriteOwnedByte(0x07C3, 7, 4);
        Assert.Equal(new ulong[] { ReviewedChargeProtocol.EncodeRead(0x07C3), 0x000407C3, ReviewedChargeProtocol.EncodeRead(0x07C3) }, backend.Packets);
        Assert.Equal(4u, backend.Registers[0x07C3]);
    }

    [Fact]
    public void ExternalPlatformChangeIsPreservedAndNotRetried()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); backend.Registers[0x07C3] = 5; backend.Packets.Clear();
        Assert.Throws<InvalidOperationException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
        Assert.Single(backend.Packets);
        Assert.Equal(5u, backend.Registers[0x07C3]);
    }

    [Fact]
    public void BatteryWritePreservesBitSevenAndToleratesItsFirmwareReadbackChange()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); backend.Registers[0x07B9] = 0x80;
        backend.AfterWrite = address => { if (address == 0x07B9) backend.Registers[address] &= 0x7F; };
        transport.WriteOwnedByte(0x07B9, 0x80, 0xD0);
        Assert.Contains(0x00D007B9UL, backend.Packets);
        Assert.Equal(80u, backend.Registers[0x07B9]);
    }

    [Fact]
    public void BatteryRawGreaterThanOneByteCannotBeUsedForWriting()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); backend.Registers[0x07B9] = 0x100;
        var writes = backend.WriteCount;
        Assert.Throws<InvalidDataException>(() => transport.WriteOwnedByte(0x07B9, 0, 80));
        Assert.Equal(writes, backend.WriteCount);
    }

    [Fact]
    public void RejectedWriteAddressDoesNotReadItOrInvokeAnyWrite()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); var count = backend.Packets.Count;
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.WriteOwnedByte(0x9164, 0, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => transport.WriteOwnedByte(0x0770, 0xFF, 4));
        Assert.Equal(count, backend.Packets.Count);
    }

    [Fact]
    public void FailedWriteReadbackDoesNotRetryAndInvalidTelemetryFailsClosed()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); backend.AfterWrite = address => backend.Registers[address] = 5;
        Assert.Throws<InvalidDataException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
        Assert.Equal(1, backend.WriteCount);
        var count = backend.Calls.Count;
        Assert.Throws<InvalidOperationException>(() => transport.ReadSnapshot());
        Assert.Equal(count, backend.Calls.Count);
        var badTelemetry = new FakeBackend { Telemetry = new(75, true, true, false, 300000, 90000) };
        using var other = Create(badTelemetry);
        Assert.Throws<InvalidDataException>(() => other.ReadSnapshot());
    }

    [Theory]
    [InlineData(null)] [InlineData(0xFEFEFEFEu)]
    public void MissingOrFailedWriteResponseQuarantinesWithoutRetry(uint? response)
    {
        var backend = new FakeBackend { WriteResponse = response }; using var transport = Create(backend);
        transport.ReadSnapshot();
        Assert.Throws<InvalidDataException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
        var count = backend.Calls.Count;
        Assert.Throws<InvalidOperationException>(() => transport.ReadByte(0x07C3));
        Assert.Throws<InvalidOperationException>(() => transport.WriteOwnedByte(0x07C3, 4, 7));
        Assert.Equal(count, backend.Calls.Count); Assert.Equal(1, backend.WriteCount);
    }

    [Fact]
    public void SubsequentSnapshotIdentityFailureInvalidatesWriteInitialization()
    {
        var backend = new FakeBackend(); using var transport = Create(backend);
        transport.ReadSnapshot(); backend.Registers[0x0454] = 0x58;
        Assert.Throws<InvalidDataException>(() => transport.ReadSnapshot());
        var count = backend.Calls.Count;
        Assert.Throws<InvalidOperationException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
        Assert.Equal(count, backend.Calls.Count); Assert.Equal(0, backend.WriteCount);
    }

    [Fact]
    public void DisposedTransportDoesNotReadOrWriteAnything()
    {
        var backend = new FakeBackend(); var transport = Create(backend); transport.Dispose();
        Assert.Throws<ObjectDisposedException>(() => transport.ReadSnapshot());
        Assert.Throws<ObjectDisposedException>(() => transport.ReadByte(0x07C3));
        Assert.Throws<ObjectDisposedException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
        Assert.Equal(["dispose"], backend.Calls);
    }

    [Fact]
    public void AcOfflineIsReportedForRestorationRatherThanConvertedToUnknown()
    {
        var backend = new FakeBackend { Telemetry = new(74, false, false, true, 0, 80000) };
        using var transport = Create(backend);
        var snapshot = transport.ReadSnapshot();
        Assert.False(snapshot.PowerOnline); Assert.True(snapshot.Discharging);
    }

    [Fact]
    public async Task DeadlinePoisonsTransportAndDoesNotQueueFurtherCallsOrLateWrites()
    {
        var backend = new FakeBackend();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        backend.IdentityDelay = () => { entered.Set(); release.Wait(TimeSpan.FromSeconds(2)); };
        using var transport = new ReviewedChargeTransport(backend, _ => { }, TimeSpan.FromMilliseconds(50));
        try
        {
            await Task.Run(() => Assert.Throws<TimeoutException>(() => transport.ReadSnapshot()));
            Assert.True(entered.IsSet);
            var count = backend.Calls.Count;
            Assert.Throws<InvalidOperationException>(() => transport.ReadSnapshot());
            Assert.Throws<InvalidOperationException>(() => transport.WriteOwnedByte(0x07C3, 7, 4));
            Assert.Equal(count, backend.Calls.Count);
            Assert.Empty(backend.Packets);
        }
        finally { release.Set(); }
    }

    private static ReviewedChargeTransport Create(FakeBackend backend) => new(backend, _ => { });

    private sealed class FakeBackend : IReviewedChargeBackend
    {
        public ChargeMachineIdentity Identity = ReviewedChargeProtocolTests.ReviewedIdentity;
        public bool Administrator = true;
        public ChargeWmiSignature Signature = new("{ABBC0F6F-8EA1-11d1-00A0-C90629100000}", "GetSetULong", 4, "Data", "UInt64", "Return", "UInt32");
        public ChargeWmiInstance[] Instances = Enumerable.Range(0, 10).Select(i => new ChargeWmiInstance($@"ACPI\PNP0C14\1_{i}", true)).ToArray();
        public ReviewedChargeTelemetry Telemetry = new(75, true, true, false, 30000, 90000);
        public Dictionary<ushort, uint> Registers = new()
        {
            [0x0740] = 0x011A, [0x0454] = 0x59, [0x0455] = 0, [0x047D] = 0,
            [0x0742] = 0x22, [0x0770] = 0xFF, [0x07C3] = 7, [0x07A6] = 0x28,
            [0x07B9] = 0, [0x07D0] = 0, [0x0490] = 0x0F, [0x0497] = 0,
            [0x04AB] = 75, [0x9164] = 0
        };
        public readonly List<string> Calls = [];
        public readonly List<ulong> Packets = [];
        public readonly List<string> InstanceNames = [];
        public readonly List<TimeSpan> Timeouts = [];
        public Func<ushort, uint?>? OnRead;
        public Action<ushort>? AfterWrite;
        public Action? IdentityDelay;
        public uint? WriteResponse = 0;
        public int WriteCount;
        public ChargeMachineIdentity ReadMachineIdentity(TimeSpan timeout)
        { Calls.Add("identity"); Timeouts.Add(timeout); IdentityDelay?.Invoke(); return Identity; }
        public bool IsAdministrator() { Calls.Add("administrator"); return Administrator; }
        public ChargeWmiSignature ReadInterfaceSignature(TimeSpan timeout)
        { Calls.Add("signature"); Timeouts.Add(timeout); return Signature; }
        public IReadOnlyList<ChargeWmiInstance> ReadInterfaceInstances(TimeSpan timeout)
        { Calls.Add("instances"); Timeouts.Add(timeout); return Instances; }
        public uint? InvokeGetSetULong(string instanceName, ulong packet, TimeSpan timeout)
        {
            Calls.Add("invoke"); Packets.Add(packet); InstanceNames.Add(instanceName); Timeouts.Add(timeout);
            var address = (ushort)packet;
            if ((packet >> 32) == 0x0100) return OnRead is null ? Registers[address] : OnRead(address);
            WriteCount++; Registers[address] = (byte)(packet >> 16); AfterWrite?.Invoke(address); return WriteResponse;
        }
        public ReviewedChargeTelemetry ReadBatteryTelemetry(TimeSpan timeout)
        { Calls.Add("telemetry"); Timeouts.Add(timeout); return Telemetry; }
        public void Dispose() { Calls.Add("dispose"); }
    }
}
