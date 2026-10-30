using System.Collections.ObjectModel;
using System.Text.Json;
using RevoHelper.App.Charging;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Tests.Charging;

// Every test in this class is memory-only. In particular, do not call session
// Initialize/Read/Save, client InspectPending/Enable/Recover, or a real transport.
public sealed class ChargeSessionContractTests
{
    [Fact]
    public void SnapshotJsonRoundTripPreservesUInt16KeysRawUInt32AndRecords()
    {
        var original = Snapshot();
        var json = JsonSerializer.Serialize(original);
        var roundTrip = Assert.IsType<ChargeEcSnapshot>(JsonSerializer.Deserialize<ChargeEcSnapshot>(json));

        Assert.Equal(original.Identity, roundTrip.Identity);
        Assert.Equal(original.ObservedAt, roundTrip.ObservedAt);
        Assert.Equal(original.WindowsPercent, roundTrip.WindowsPercent);
        Assert.Equal(original.PowerOnline, roundTrip.PowerOnline);
        Assert.Equal(original.Charging, roundTrip.Charging);
        Assert.Equal(original.Discharging, roundTrip.Discharging);
        Assert.Equal(original.ChargeRateMw, roundTrip.ChargeRateMw);
        Assert.Equal(original.RemainingCapacity, roundTrip.RemainingCapacity);
        Assert.Equal(original.Registers.OrderBy(pair => pair.Key), roundTrip.Registers.OrderBy(pair => pair.Key));
        Assert.Equal(0xABCD811Au, roundTrip.Registers[0x0740]);
        Assert.Equal((byte)0x1A, roundTrip.Byte(0x0740));
        Assert.Equal(0u, roundTrip.Registers[0x9164]);
        ChargeSessionEngine.ValidateInitial(roundTrip);
    }

    [Theory]
    [InlineData(60)] [InlineData(80)]
    public void RecoveryLeaseJsonRoundTripUsesOnlyMemoryAndRetainsTheOriginalSnapshot(int limit)
    {
        var original = new ChargeLease("bb01ce2a69fe4a9f8875cebd148052a1", limit,
            100, 638973000000000001L, 200, 638973000000000002L, 300, 638973000000000003L, Snapshot());
        var json = JsonSerializer.Serialize(original);
        var roundTrip = Assert.IsType<ChargeLease>(JsonSerializer.Deserialize<ChargeLease>(json));

        Assert.Equal(original.SessionId, roundTrip.SessionId);
        Assert.Equal(original.LimitPercent, roundTrip.LimitPercent);
        Assert.Equal(original.OwnerId, roundTrip.OwnerId);
        Assert.Equal(original.OwnerStartTicks, roundTrip.OwnerStartTicks);
        Assert.Equal(original.GuardianId, roundTrip.GuardianId);
        Assert.Equal(original.GuardianStartTicks, roundTrip.GuardianStartTicks);
        Assert.Equal(original.WorkerId, roundTrip.WorkerId);
        Assert.Equal(original.WorkerStartTicks, roundTrip.WorkerStartTicks);
        Assert.Equal(original.Original.Identity, roundTrip.Original.Identity);
        Assert.Equal(original.Original.Registers.OrderBy(pair => pair.Key), roundTrip.Original.Registers.OrderBy(pair => pair.Key));
        ChargeSessionEngine.ValidateInitial(roundTrip.Original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("../bb01ce2a69fe4a9f8875cebd148052a1")]
    [InlineData(@"..\bb01ce2a69fe4a9f8875cebd148052a1")]
    [InlineData(@"C:\Windows")]
    [InlineData("bb01ce2a-69fe-4a9f-8875-cebd148052a1")]
    [InlineData("BB01CE2A69FE4A9F8875CEBD148052A1")]
    [InlineData("bb01ce2a69fe4a9f8875cebd148052a1 ")]
    [InlineData("bb01ce2a69fe4a9f8875cebd148052a1/child")]
    public void SessionPathRejectsNonCanonicalOrPathLikeIdentifiersWithoutFileIo(string session) =>
        Assert.Throws<ArgumentException>(() => new ChargeSessionFiles(session));

    [Fact]
    public void CanonicalSessionIdentifierOnlyBuildsBoundedPathsWithoutInitializingThem()
    {
        const string session = "bb01ce2a69fe4a9f8875cebd148052a1";
        var files = new ChargeSessionFiles(session);
        Assert.Equal(session, files.SessionId);
        Assert.Equal(Path.Combine(ChargeSessionFiles.Root, session), files.DirectoryPath);
        Assert.Equal(Path.Combine(files.DirectoryPath, "lease.json"), files.LeasePath);
        Assert.Equal(Path.Combine(files.DirectoryPath, "status.json"), files.StatusPath);
        Assert.Equal(@"Local\RevoHelper.ExperimentalCharge.Stop." + session, files.StopName);
        Assert.Equal(@"Local\RevoHelper.ExperimentalCharge.Start." + session, files.StartName);
        // No Directory.Exists/File.Exists check: these tests must not inspect the
        // user's real sessions, and no Initialize method is called.
    }

    [Fact]
    public void FreshClientRequestStopAndDisposeDoNotEnableOrInspectSessions()
    {
        var client = new ExperimentalChargeClient();
        var notifications = 0;
        client.StatusChanged += (_, _) => notifications++;
        AssertInactive();
        client.RequestStop("Memory-only test; no session exists.");
        AssertInactive();
        client.Dispose();
        client.Dispose();
        AssertInactive();
        Assert.Equal(0, notifications);

        void AssertInactive()
        {
            Assert.False(client.Status.Enabled);
            Assert.False(client.Status.Busy);
            Assert.False(client.Status.RecoveryPending);
            Assert.Null(client.Status.SessionId);
        }
    }

    private static ChargeEcSnapshot Snapshot() => new(
        new("MECHREVO", "YAOSHI Series", "N.1.18MRO24", 1, 25),
        new ReadOnlyDictionary<ushort, uint>(new Dictionary<ushort, uint>
        {
            [0x0740] = 0xABCD811A, [0x0454] = 0x59, [0x0455] = 0, [0x047D] = 0,
            [0x0742] = 0x22, [0x0770] = 0xFF, [0x07C3] = 7, [0x07A6] = 0x28,
            [0x07B9] = 0, [0x07D0] = 0, [0x0490] = 0x0F, [0x0497] = 0,
            [0x04AB] = 74, [0x9164] = 0
        }), 74, true, true, false, 30000, 90000,
        new DateTimeOffset(2026, 10, 30, 18, 30, 0, TimeSpan.Zero));
}
