using RevoHelper.Hardware.Windows.BatteryCharge;
using RevoHelper.Hardware.Windows.GpuPower;

namespace RevoHelper.Hardware.Windows.Tests.GpuPower;

public sealed class GpuPowerEcReaderTests
{
    [Fact]
    public void ReadsTheFixedListOnceEachWithReadPacketsOnly()
    {
        var backend = new FakeBackend();
        var readout = Create(backend).Read(CancellationToken.None);

        Assert.Null(readout.Failure);
        Assert.Empty(readout.Unread);
        Assert.Equal(GpuPowerEcReader.Addresses.Count, backend.Packets.Count);
        Assert.All(backend.Packets, packet => Assert.Equal(0x0100UL, packet >> 32));
        Assert.Equal(GpuPowerEcReader.Addresses, backend.Packets.Select(packet => (ushort)packet));
        Assert.All(backend.InstanceNames, name => Assert.Equal(@"ACPI\PNP0C14\1_0", name));
        Assert.Equal((byte)0x60, readout[0x07C8]);
        Assert.Equal((byte)0x32, readout[0x91C0]);
    }

    [Fact]
    public void TheListCoversTheStateBytesAndEightBytesOfEachTableRow()
    {
        Assert.Equal(48, GpuPowerEcReader.TableAddresses.Count);
        Assert.Equal((ushort)0x9190, GpuPowerEcReader.TableAddresses[0]);
        Assert.Equal((ushort)0x91E7, GpuPowerEcReader.TableAddresses[^1]);
        Assert.DoesNotContain((ushort)0x9198, GpuPowerEcReader.TableAddresses);
        Assert.Contains((ushort)0x0A6E, GpuPowerEcReader.StateAddresses);
        Assert.Equal(GpuPowerEcReader.Addresses.Count, GpuPowerEcReader.Addresses.Distinct().Count());
    }

    [Theory]
    [InlineData(0x07B9)] // charge threshold
    [InlineData(0x07F4)] // hold countdown
    [InlineData(0x0FDF)] // firmware row loader
    [InlineData(0xFFFF)]
    public void AddressesOutsideTheListAreRefusedBeforeAnyCall(int address) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GpuPowerEcReader.EncodeRead((ushort)address));

    [Fact]
    public void ABusyFirmwareIsReadAgainAndTheValueKept()
    {
        var backend = new FakeBackend();
        var busy = 2;
        backend.OnRead = address => address == 0x09EA && busy-- > 0 ? ReviewedChargeProtocol.FailureSentinel : null;
        var pauses = new List<TimeSpan>();

        var readout = Create(backend, pauses.Add).Read(CancellationToken.None);

        Assert.Equal((byte)0x32, readout[0x09EA]);
        Assert.Equal(2, readout.BusyRetries);
        Assert.Equal(2, pauses.Count(pause => pause == GpuPowerEcReader.BusyPause));
        Assert.Equal(GpuPowerEcReader.Addresses.Count + 2, backend.Packets.Count);
    }

    [Fact]
    public void BusyUpperBytesDoNotHideTheLowByte()
    {
        var backend = new FakeBackend();
        // Seen on this machine: another caller's busy answer lands in the upper bytes (91CE=FEFE0000).
        backend.OnRead = address => address == 0x09EC ? 0xFEFE0005u : null;

        var readout = Create(backend).Read(CancellationToken.None);

        Assert.Equal((byte)0x05, readout[0x09EC]);
        Assert.Equal(0, readout.BusyRetries);
    }

    [Fact]
    public void AnAddressThatStaysBusyIsLeftUnreadAndTheRestContinue()
    {
        var backend = new FakeBackend();
        backend.OnRead = address => address == 0x0788 ? ReviewedChargeProtocol.FailureSentinel : null;

        var readout = Create(backend).Read(CancellationToken.None);

        Assert.Null(readout[0x0788]);
        Assert.Equal([(ushort)0x0788], readout.Unread);
        Assert.Equal(GpuPowerEcReader.MaxAttempts - 1, readout.BusyRetries);
        Assert.Equal((byte)0xCD, readout[0x9062]);
        Assert.Null(readout.Failure);
    }

    [Fact]
    public void ADeadlineStopsTheReadAndKeepsWhatWasRead()
    {
        var backend = new FakeBackend { BlockAt = 0x09EA };
        var readout = Create(backend, timeout: TimeSpan.FromMilliseconds(100)).Read(CancellationToken.None);

        Assert.Equal("EC 读取超时，已停止后续读取", readout.Failure);
        Assert.Equal((byte)0x07, readout[0x0490]);
        Assert.Null(readout[0x09EA]);
        Assert.Contains((ushort)0x91C0, readout.Unread);
        Assert.Equal((ushort)0x09EA, (ushort)backend.Packets[^1]);
        backend.Release.Set();
    }

    [Fact]
    public void NoReadsWithoutAdministratorOrTheReviewedInterface()
    {
        var notAdministrator = new FakeBackend { Administrator = false };
        Assert.Equal("需要以管理员身份运行", Create(notAdministrator).Read(CancellationToken.None).Failure);
        Assert.Empty(notAdministrator.Packets);

        var otherInterface = new FakeBackend { Signature = FakeBackend.ReviewedSignature with { MethodId = 5 } };
        Assert.Equal("EC 接口与已核对的定义不一致，未读取", Create(otherInterface).Read(CancellationToken.None).Failure);
        Assert.Empty(otherInterface.Packets);

        var twoInstances = new FakeBackend { Instances = [new(@"ACPI\PNP0C14\1_0", true), new(@"ACPI\PNP0C14\2_0", true)] };
        Assert.Equal("EC 接口不可用（InvalidDataException）", Create(twoInstances).Read(CancellationToken.None).Failure);
        Assert.Empty(twoInstances.Packets);
    }

    [Fact]
    public void ConstructionDoesNothing()
    {
        var created = 0;
        _ = new GpuPowerEcReader(() => { created++; return new FakeBackend(); });
        Assert.Equal(0, created);
    }

    private static GpuPowerEcReader Create(FakeBackend backend, Action<TimeSpan>? pause = null, TimeSpan? timeout = null) =>
        new(() => backend, pause ?? (_ => { }), timeout);

    internal sealed class FakeBackend : IReviewedChargeBackend
    {
        public static readonly ChargeWmiSignature ReviewedSignature =
            new("{ABBC0F6F-8EA1-11d1-00A0-C90629100000}", "GetSetULong", 4, "Data", "UInt64", "Return", "UInt32");

        // This machine (MRO24) in office mode on mains power, 2026-11-14.
        public static readonly IReadOnlyDictionary<ushort, byte> Office = new Dictionary<ushort, byte>
        {
            [0x0490] = 0x07, [0x0456] = 0xC8, [0x0741] = 0x01, [0x0727] = 0x88, [0x072B] = 0x01, [0x07C4] = 0x38,
            [0x07C7] = 0x0E, [0x07C8] = 0x60, [0x0A6E] = 0x00, [0x07D2] = 0x1B,
            [0x0743] = 0, [0x0744] = 0, [0x0745] = 0, [0x0746] = 0, [0x0747] = 0,
            [0x09EA] = 0x32, [0x0788] = 0x32, [0x09EB] = 0x00, [0x07D4] = 0x00, [0x09EC] = 0x05, [0x07D5] = 0x05,
            [0x07F7] = 0xCD, [0x9062] = 0xCD, [0x0763] = 0x3C, [0x0764] = 0x3C, [0x07D6] = 0, [0x07D7] = 0,
        }.Concat(Rows()).ToDictionary(pair => pair.Key, pair => pair.Value);

        public bool Administrator = true;
        public ChargeWmiSignature Signature = ReviewedSignature;
        public ChargeWmiInstance[] Instances = Enumerable.Range(0, 4).Select(i => new ChargeWmiInstance($@"ACPI\PNP0C14\1_{i}", true)).ToArray();
        public Func<ushort, uint?>? OnRead;
        public ushort? BlockAt;
        public readonly ManualResetEventSlim Release = new();
        public readonly List<ulong> Packets = [];
        public readonly List<string> InstanceNames = [];

        public bool IsAdministrator() => Administrator;
        public ChargeWmiSignature ReadInterfaceSignature(TimeSpan timeout) => Signature;
        public IReadOnlyList<ChargeWmiInstance> ReadInterfaceInstances(TimeSpan timeout) => Instances;

        public uint? InvokeGetSetULong(string instanceName, ulong packet, TimeSpan timeout)
        {
            lock (Packets) { Packets.Add(packet); InstanceNames.Add(instanceName); }
            if (packet >> 32 != 0x0100) throw new InvalidOperationException("Only reads are expected.");
            var address = (ushort)packet;
            if (address == BlockAt) Release.Wait(TimeSpan.FromSeconds(5));
            if (OnRead?.Invoke(address) is { } answer) return answer;
            // The firmware also returns the next byte above the asked one.
            var next = Office.TryGetValue((ushort)(address + 1), out var high) ? high : (byte)0;
            return Office.TryGetValue(address, out var low) ? (uint)(low | next << 8) : 0u;
        }

        public ReviewedChargeTelemetry ReadBatteryTelemetry(TimeSpan timeout) =>
            throw new InvalidOperationException("The GPU reader never reads battery telemetry.");

        public void Dispose() { }

        private static IEnumerable<KeyValuePair<ushort, byte>> Rows()
        {
            byte[][] rows =
            [
                [0x32, 0x05, 0x32, 0x05, 0x2D, 0x2D, 0x37, 0x00],
                [0x37, 0x19, 0x37, 0x19, 0x41, 0x41, 0x64, 0x00],
                [0x37, 0x19, 0x32, 0x05, 0x32, 0x32, 0x55, 0x00],
                [0x32, 0x05, 0x32, 0x05, 0x3C, 0x3C, 0xCD, 0x00],
                [0x37, 0x19, 0x37, 0x19, 0x6E, 0x6E, 0xC8, 0x00],
                [0x37, 0x19, 0x37, 0x19, 0x5A, 0x5A, 0xC8, 0x00]
            ];
            for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                yield return new((ushort)(0x9190 + row * 0x10 + column), rows[row][column]);
        }
    }
}
