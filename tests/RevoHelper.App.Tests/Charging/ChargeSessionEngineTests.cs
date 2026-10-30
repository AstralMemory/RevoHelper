using RevoHelper.App.Charging;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Tests.Charging;

public sealed class ChargeSessionEngineTests
{
    [Fact]
    public void ConstructionDoesNotCreateTransportOrEnableAnything()
    {
        var fixture = new Fixture();
        _ = fixture.Engine;
        Assert.Equal(0, fixture.Created);
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void InitialReviewedProfileAndUntouchedNativeWorkstationStateAreAccepted()
    {
        var fixture = new Fixture();
        ChargeSessionEngine.ValidateInitial(fixture.Snapshot());
        Assert.Empty(fixture.Writes);
    }

    [Theory]
    [InlineData(0x07C3, 4)] [InlineData(0x0770, 4)]
    [InlineData(0x07B9, 80)] [InlineData(0x07B9, 0x100)] [InlineData(0x07D0, 60)]
    [InlineData(0x9164, 80)] [InlineData(0x07A6, 0x18)] [InlineData(0x0742, 0x26)]
    [InlineData(0x0490, 0x0E)] [InlineData(0x0490, 0x0B)]
    public void InitialStateNeverTakesOverAnExistingControlSetting(int address, uint value)
    {
        var fixture = new Fixture(); fixture.Registers[(ushort)address] = value;
        Assert.Throws<InvalidOperationException>(() => ChargeSessionEngine.ValidateInitial(fixture.Snapshot()));
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void InitialDifferentSystemLabelsAreAcceptedButDisconnectedPowerIsRejected()
    {
        var fixture = new Fixture();
        ChargeSessionEngine.ValidateInitial(fixture.Snapshot() with
        {
            Identity = new("MECHREVO", "AURORA X Series", "OTHER-BIOS", 2, 99)
        });
        Assert.Empty(fixture.Writes);
        fixture.Online = false;
        Assert.Throws<InvalidOperationException>(() => ChargeSessionEngine.ValidateInitial(fixture.Snapshot()));
    }

    [Fact]
    public void DifferentLaptopCanApplyAndRestoreWithoutAnyFirmwareIdentityRegisters()
    {
        var fixture = new Fixture { Identity = new("MECHREVO", "WU JIE Series", "OTHER-BIOS", 2, 99) };
        foreach (var address in new ushort[] { 0x0740, 0x0454, 0x0455, 0x047D }) fixture.Registers.Remove(address);
        fixture.AfterWait = () => fixture.Stop = true;
        var result = fixture.Run(60);
        Assert.True(result.CriticalRestored);
        Assert.Equal(new byte[] { 60, 4, 0, 7 }, fixture.Writes.Select(write =>
            write.Address == 0x07B9 ? (byte)(write.Target & 0x7F) : write.Target));
    }

    [Fact]
    public void AChangedInterfaceRouteBeforeRunIsRejectedBeforeAnyWrite()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot();
        fixture.InterfaceInstanceName = @"ACPI\PNP0C14\2_0";
        Assert.Throws<InvalidDataException>(() => fixture.Run(original: original));
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void RecoveryDoesNotWriteToADifferentInterfaceRoute()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.InterfaceInstanceName = @"ACPI\PNP0C14\2_0";
        var result = fixture.Recover(original);
        Assert.False(result.CriticalRestored);
        Assert.Empty(fixture.Writes);
        Assert.Equal(4u, fixture.Registers[0x07C3]);
        Assert.Equal(80u, fixture.Registers[0x07B9]);
    }

    [Theory]
    [InlineData(null, @"ACPI\PNP0C14\1_0")]
    [InlineData(@"ACPI\PNP0C14\2_0", @"ACPI\PNP0C14\2_0")]
    public void RecoverySupportsLegacyDefaultAndExplicitAlternativeRoutes(string? savedRoute, string liveRoute)
    {
        var fixture = new Fixture { InterfaceInstanceName = liveRoute };
        var original = fixture.Snapshot() with { InterfaceInstanceName = savedRoute }; fixture.MakeActive(80);
        var result = fixture.Recover(original);
        Assert.True(result.CriticalRestored);
    }

    [Theory]
    [InlineData(0x07C3, 7)] [InlineData(0x0770, 4)] [InlineData(0x07D0, 60)]
    [InlineData(0x07A6, 0x18)] [InlineData(0x0742, 0x66)] [InlineData(0x07B9, 60)]
    [InlineData(0x9164, 60)] [InlineData(0x0490, 0x0E)]
    public void ActiveExternalChangesAreRejectedInsteadOfContinuouslyOverwritten(int address, uint value)
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.Registers[(ushort)address] = value;
        Assert.Throws<InvalidOperationException>(() => ChargeSessionEngine.ValidateActive(fixture.Snapshot(), original, 80));
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void ActivePendingCacheAndFirmwareOwnedThresholdBitAreAccepted()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.Registers[0x07B9] = 0xD0; fixture.Registers[0x9164] = 0;
        ChargeSessionEngine.ValidateActive(fixture.Snapshot(), original, 80);
        fixture.Registers[0x9164] = 80;
        ChargeSessionEngine.ValidateActive(fixture.Snapshot(), original, 80);
    }

    [Theory]
    [InlineData(0)] [InlineData(59)] [InlineData(75)] [InlineData(100)]
    public void RunRejectsAllUnreviewedThresholdsBeforeCreatingTransport(int limit)
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Run(limit));
        Assert.Equal(0, fixture.Created); Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void RunRevalidatesLiveInitialStateInsteadOfTrustingSavedOriginal()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.Registers[0x07C3] = 5;
        Assert.Throws<InvalidOperationException>(() => fixture.Run(original: original));
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void ApplyJournalFailureCausesZeroWrites()
    {
        var fixture = new Fixture { JournalFailure = phase => phase == "ThresholdApplyIntent" };
        Assert.Throws<IOException>(() => fixture.Run());
        Assert.Empty(fixture.Writes);
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(0u, fixture.Registers[0x07B9]);
    }

    [Fact]
    public void StopBeforeFirstWriteLeavesEverythingUntouched()
    {
        var fixture = new Fixture { Stop = true };
        var result = fixture.Run();
        Assert.True(result.CriticalRestored); Assert.True(result.CacheMatchesOriginal);
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public void StopAfterThresholdWriteThrowsCancellationOnlyAfterFinallyRestoresSharedState()
    {
        var fixture = new Fixture();
        fixture.ThrowAfterWrite = write =>
        {
            if (write.Address == 0x07B9 && (write.Target & 0x7F) != 0) fixture.Stop = true;
            return false;
        };
        Assert.Throws<OperationCanceledException>(() => fixture.Run());
        Assert.Equal(new ushort[] { 0x07B9, 0x07B9 }, fixture.Writes.Select(write => write.Address));
        Assert.Equal(0u, fixture.Registers[0x07B9] & 0x7F);
        Assert.Equal(7u, fixture.Registers[0x07C3]);
        Assert.Equal(0u, fixture.Registers[0x9164]);
        Assert.DoesNotContain("journal:PlatformApplyIntent", fixture.Events);
    }

    [Theory]
    [InlineData(60)] [InlineData(80)]
    public void NormalSessionJournalsBeforeWritesAndRestoresThresholdBeforePlatform(int limit)
    {
        var fixture = new Fixture();
        fixture.AfterWait = () => fixture.Stop = true;
        var result = fixture.Run(limit);
        Assert.True(result.CriticalRestored); Assert.True(result.CacheMatchesOriginal); Assert.Empty(result.Errors);
        Assert.Equal(new ushort[] { 0x07B9, 0x07C3, 0x07B9, 0x07C3 }, fixture.Writes.Select(write => write.Address));
        Assert.Equal(new byte[] { (byte)limit, 4, 0, 7 },
            fixture.Writes.Select(write => write.Address == 0x07B9 ? (byte)(write.Target & 0x7F) : write.Target));
        Assert.True(fixture.Events.IndexOf("journal:ThresholdApplyIntent") < fixture.Events.IndexOf("write:07B9:0"));
        Assert.True(fixture.Events.IndexOf("journal:PlatformApplyIntent") < fixture.Events.IndexOf("write:07C3:1"));
        Assert.True(fixture.Events.IndexOf("journal:ThresholdRestoreIntent") < fixture.Events.IndexOf("write:07B9:2"));
        Assert.True(fixture.Events.IndexOf("journal:PlatformRestoreIntent") < fixture.Events.IndexOf("write:07C3:3"));
        Assert.All(fixture.Writes, write => Assert.Contains(write.Address, new ushort[] { 0x07B9, 0x07C3 }));
        Assert.NotEqual(fixture.Writes[1].TransportId, fixture.Writes[3].TransportId);
    }

    [Fact]
    public void SignalledStopDoesNotSkipIndependentFirmwareCacheRecoveryWait()
    {
        var fixture = new Fixture { SkipPollingWaitAfterStop = true };
        fixture.AfterWait = () => fixture.Stop = true;
        var recoveryWaits = 0;
        fixture.RecoveryWait = () => { recoveryWaits++; fixture.AdvanceFirmware(); };
        var result = fixture.Run();
        Assert.Equal(1, recoveryWaits);
        Assert.True(result.CriticalRestored);
        Assert.True(result.CacheMatchesOriginal);
        Assert.Equal(0u, fixture.Registers[0x9164]);
        Assert.Equal(7u, fixture.Registers[0x07C3]);
    }

    [Fact]
    public void FirmwareTickCanReportActualStoppedChargingWithoutTreatingThresholdEchoAsProof()
    {
        var fixture = new Fixture { Soc = 74 };
        var published = new List<ChargeEcSnapshot>();
        fixture.Published = snapshot => published.Add(snapshot);
        fixture.AfterWait = () => { if (fixture.WaitCount >= 2) fixture.Stop = true; };
        var result = fixture.Run(60);
        Assert.True(result.CriticalRestored);
        Assert.True(published[0].Charging); // First loop preceded a simulated firmware tick.
        Assert.Contains(published, snapshot => !snapshot.Charging && snapshot.ChargeRateMw == 0
            && (snapshot.Byte(0x07B9) & 0x80) != 0 && (snapshot.Byte(0x0742) & 4) != 0);
    }

    [Theory]
    [InlineData(0x07B9)] [InlineData(0x07C3)]
    public void AmbiguousWriteThatMutatesThenThrowsStillGetsIndependentRecovery(int address)
    {
        var fixture = new Fixture(); var thrown = false;
        fixture.ThrowAfterWrite = write => write.Address == address && !thrown && (thrown = true);
        Assert.Throws<IOException>(() => fixture.Run());
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(0u, fixture.Registers[0x07B9] & 0x7F);
        var failed = fixture.Writes.First(write => write.Address == address);
        Assert.Contains(fixture.Writes, write => write.Address == address && write.TransportId != failed.TransportId);
    }

    [Fact]
    public void PlatformIntentJournalFailureRestoresAlreadyAppliedThresholdWithoutTouchingPlatform()
    {
        var fixture = new Fixture { JournalFailure = phase => phase == "PlatformApplyIntent" };
        Assert.Throws<IOException>(() => fixture.Run());
        Assert.Equal(new ushort[] { 0x07B9, 0x07B9 }, fixture.Writes.Select(write => write.Address));
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(0u, fixture.Registers[0x07B9]);
    }

    [Fact]
    public void ThresholdRecoveryFailureDoesNotPreventIndependentPlatformRecovery()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.RejectBeforeWrite = write => write.Address == 0x07B9 && (write.Target & 0x7F) == 0;
        var result = fixture.Recover(original);
        Assert.True(result.PlatformRestored); Assert.False(result.ThresholdRestored);
        Assert.Contains(result.Errors, error => error.StartsWith("阈值恢复：", StringComparison.Ordinal));
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(80u, fixture.Registers[0x07B9]);
        var threshold = fixture.Writes.Single(write => write.Address == 0x07B9);
        var platform = fixture.Writes.Single(write => write.Address == 0x07C3);
        Assert.NotEqual(threshold.TransportId, platform.TransportId);
    }

    [Fact]
    public void ThresholdReadFailureStillAllowsFreshChannelPlatformRecovery()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.FailRead = (_, address) => address == 0x07B9;
        var result = fixture.Recover(original);
        Assert.True(result.PlatformRestored); Assert.False(result.ThresholdRestored);
        Assert.Equal(7u, fixture.Registers[0x07C3]);
        Assert.Single(fixture.Writes); Assert.Equal((ushort)0x07C3, fixture.Writes[0].Address);
    }

    [Fact]
    public void ExternalThresholdTakeoverIsNotOverwrittenButOwnedPlatformIsRestored()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.Registers[0x07B9] = 70;
        var result = fixture.Recover(original);
        Assert.False(result.ThresholdRestored); Assert.True(result.PlatformRestored);
        Assert.Equal(70u, fixture.Registers[0x07B9]);
        Assert.DoesNotContain(fixture.Writes, write => write.Address == 0x07B9);
    }

    [Fact]
    public void ExternalPlatformTakeoverIsNotOverwrittenButOwnedThresholdIsRestored()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.Registers[0x07C3] = 5;
        var result = fixture.Recover(original);
        Assert.False(result.PlatformRestored); Assert.True(result.ThresholdRestored);
        Assert.Equal(5u, fixture.Registers[0x07C3]);
        Assert.DoesNotContain(fixture.Writes, write => write.Address == 0x07C3);
    }

    [Fact]
    public void DisconnectDuringSessionTriggersRecoveryWithoutRequiringAcForReadByte()
    {
        var fixture = new Fixture(); fixture.AfterWait = () => fixture.Online = false;
        Assert.Throws<InvalidOperationException>(() => fixture.Run());
        Assert.False(fixture.Online);
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(0u, fixture.Registers[0x07B9] & 0x7F);
    }

    [Fact]
    public void OwnerDeathDuringSessionTriggersRecoveryAndDoesNotReapply()
    {
        var fixture = new Fixture(); fixture.AfterWait = () => fixture.OwnerAlive = false;
        Assert.Throws<InvalidOperationException>(() => fixture.Run());
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(0u, fixture.Registers[0x07B9] & 0x7F);
        Assert.Equal(4, fixture.Writes.Count);
    }

    [Fact]
    public void EmergencyRestoresPlatformBeforeThresholdAndReportsUnrestoredCacheHonestly()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        var result = fixture.Recover(original, emergency: true);
        Assert.Equal(new ushort[] { 0x07C3, 0x07B9 }, fixture.Writes.Select(write => write.Address));
        Assert.True(result.CriticalRestored); Assert.False(result.CacheMatchesOriginal);
        Assert.Equal(80u, fixture.Registers[0x9164]);
        Assert.DoesNotContain(fixture.Writes, write => write.Address == 0x9164);
    }

    [Fact]
    public void RestorationJournalFailureDoesNotBlockRestoringEitherOwnedByte()
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(80);
        fixture.JournalFailure = _ => true;
        var result = fixture.Recover(original);
        Assert.True(result.CriticalRestored); Assert.True(result.CacheMatchesOriginal);
        Assert.Equal(2, fixture.Writes.Count);
    }

    [Theory]
    [InlineData(60)] [InlineData(80)]
    public void RestorationPreservesFirmwareOwnedThresholdBitSeven(int limit)
    {
        var fixture = new Fixture(); var original = fixture.Snapshot(); fixture.MakeActive(limit);
        fixture.Registers[0x07B9] |= 0x80;
        var result = fixture.Recover(original, limit);
        var restore = fixture.Writes.Single(write => write.Address == 0x07B9);
        Assert.Equal((byte)(0x80 | limit), restore.Expected); Assert.Equal((byte)0x80, restore.Target);
        Assert.True(result.ThresholdRestored);
    }

    [Fact]
    public void ClosedFirmwareGateStopsAfterThreeObservationsRatherThanRewritingPlatform()
    {
        var fixture = new Fixture { SimulateFirmwareGate = false };
        var published = 0; fixture.Published = _ => published++;
        Assert.Throws<InvalidOperationException>(() => fixture.Run());
        Assert.Equal(2, published); // The third failed gate observation aborts before publish.
        Assert.Equal(2, fixture.Writes.Count(write => write.Address == 0x07C3)); // One apply, one restore.
        Assert.Equal(7u, fixture.Registers[0x07C3]); Assert.Equal(0u, fixture.Registers[0x07B9] & 0x7F);
    }

    private sealed record WriteAttempt(int TransportId, ushort Address, byte Expected, byte Target);

    private sealed class Fixture
    {
        public ChargeMachineIdentity Identity = new("MECHREVO", "YAOSHI Series", "N.1.18MRO24", 1, 25);
        public string InterfaceInstanceName = ReviewedChargeProtocol.LogicalZeroInstance;
        public readonly Dictionary<ushort, uint> Registers = new()
        {
            [0x0740] = 0x011A, [0x0454] = 0x59, [0x0455] = 0, [0x047D] = 0,
            [0x0742] = 0x22, [0x0770] = 0xFF, [0x07C3] = 7, [0x07A6] = 0x28,
            [0x07B9] = 0, [0x07D0] = 0, [0x0490] = 0x0F, [0x0497] = 0,
            [0x04AB] = 74, [0x9164] = 0
        };
        public readonly List<WriteAttempt> Writes = [];
        public readonly List<string> Events = [];
        public bool Stop;
        public bool Online = true;
        public bool OwnerAlive = true;
        public bool Charging = true;
        public uint Rate = 30000;
        public int Soc = 74;
        public bool SimulateFirmwareGate = true;
        public int Created;
        public int WaitCount;
        public Action? AfterWait;
        public Action? RecoveryWait;
        public bool SkipPollingWaitAfterStop;
        public Action<ChargeEcSnapshot>? Published;
        public Func<string, bool>? JournalFailure;
        public Func<WriteAttempt, bool>? RejectBeforeWrite;
        public Func<WriteAttempt, bool>? ThrowAfterWrite;
        public Func<int, ushort, bool>? FailRead;
        public ChargeSessionEngine Engine => new(CreateTransport);

        public ChargeEcSnapshot Snapshot() => new(Identity, new Dictionary<ushort, uint>(Registers), Soc,
            Online, Charging, false, Rate, 90000, DateTimeOffset.UtcNow, InterfaceInstanceName);

        public ChargeRecoveryResult Run(int limit = 80, ChargeEcSnapshot? original = null) =>
            Engine.Run(original ?? Snapshot(), limit, () => Stop, CheckOwners,
                () => { if (!SkipPollingWaitAfterStop || !Stop) Wait(); }, Journal,
                snapshot => Published?.Invoke(snapshot), recoveryWait: RecoveryWait);

        public ChargeRecoveryResult Recover(ChargeEcSnapshot original, int limit = 80, bool emergency = false) =>
            Engine.Recover(original, limit, thresholdAttempted: true, platformAttempted: true, emergency, Journal, Wait);

        public void MakeActive(int limit)
        {
            Registers[0x07C3] = 4; Registers[0x07B9] = (uint)limit;
            Registers[0x9164] = (uint)limit; Registers[0x0742] |= 4;
        }

        private IReviewedChargeTransport CreateTransport() => new FakeTransport(this, ++Created);

        private void CheckOwners()
        {
            if (!OwnerAlive) throw new InvalidOperationException("Simulated UI/guard owner process is gone.");
        }

        private void Journal(string phase, ushort? address, int? from, int? to)
        {
            Events.Add("journal:" + phase);
            if (JournalFailure?.Invoke(phase) == true) throw new IOException("Simulated journal failure.");
        }

        private void Wait()
        {
            WaitCount++;
            if (Registers[0x07C3] == 4 && SimulateFirmwareGate)
            {
                Registers[0x0742] |= 4;
                var threshold = Registers[0x07B9] & 0x7F;
                Registers[0x9164] = threshold;
                if (threshold != 0 && Soc >= threshold)
                {
                    Registers[0x07B9] |= 0x80; Charging = false; Rate = 0;
                }
            }
            else Registers[0x0742] &= 0xFB;
            AfterWait?.Invoke();
            if (WaitCount > 8) throw new InvalidOperationException("Test observation budget exhausted.");
        }

        public void AdvanceFirmware() => Wait();

        private sealed class FakeTransport(Fixture owner, int id) : IReviewedChargeTransport
        {
            private bool disposed;
            private bool quarantined;
            public void VerifySessionOrigin(ChargeEcSnapshot original)
            {
                Check();
                if (!string.Equals(owner.InterfaceInstanceName, ReviewedChargeProtocol.SessionInstance(original), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Simulated session route changed.");
            }
            public ChargeEcSnapshot ReadSnapshot() { Check(); return owner.Snapshot(); }
            public uint ReadByte(ushort address)
            {
                Check(); ReviewedChargeProtocol.EncodeRead(address);
                if (owner.FailRead?.Invoke(id, address) == true) throw new IOException("Simulated read failure.");
                return owner.Registers[address];
            }
            public void WriteOwnedByte(ushort address, byte expected, byte target)
            {
                Check(); ReviewedChargeProtocol.EncodeOwnedWrite(address, expected, target);
                if ((byte)owner.Registers[address] != expected) throw new InvalidOperationException("Simulated external owner changed the byte.");
                var attempt = new WriteAttempt(id, address, expected, target);
                owner.Events.Add($"write:{address:X4}:{owner.Writes.Count}");
                owner.Writes.Add(attempt);
                if (owner.RejectBeforeWrite?.Invoke(attempt) == true)
                { quarantined = true; throw new IOException("Simulated rejected write; channel quarantined."); }
                owner.Registers[address] = target;
                if (owner.ThrowAfterWrite?.Invoke(attempt) == true)
                { quarantined = true; throw new IOException("Simulated write applied but acknowledgement failed."); }
            }
            private void Check()
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (quarantined) throw new InvalidOperationException("Simulated transport is quarantined.");
            }
            public void Dispose() { disposed = true; }
        }
    }
}
