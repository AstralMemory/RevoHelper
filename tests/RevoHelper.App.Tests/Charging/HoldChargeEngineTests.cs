using RevoHelper.App.Charging;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Tests.Charging;

/// <summary>
/// The fake firmware follows the routine read from the EC image: while the countdown is non-zero,
/// no charging at or below the threshold and a forced discharge above it; the countdown ticks down.
/// </summary>
public sealed class HoldChargeEngineTests
{
    [Fact]
    public void AboveTheLimit_ItHoldsAtTheCurrentChargeAndNeverAsksForADischarge()
    {
        var firmware = new Firmware { Soc = 95 };
        var seen = firmware.Run(80, polls: 12);

        Assert.All(seen.Where(o => o.Soc >= 0), o => Assert.True(o.Holding));
        Assert.Equal(95, firmware.Soc);                     // not charged, not discharged
        Assert.Equal(0, firmware.ForcedDischarges);
        Assert.Contains(seen, o => o is { Charging: false, ChargeRateMw: 0, Holding: true });
        firmware.AssertRestored();
    }

    [Fact]
    public void BelowTheLimit_ItChargesNormallyThenStopsAtTheLimit()
    {
        var firmware = new Firmware { Soc = 76 };
        firmware.Run(80, polls: 10);
        Assert.InRange(firmware.Soc, 80, 81);                // one poll may pass before the hold lands
        Assert.Equal(0, firmware.ForcedDischarges);
        firmware.AssertRestored();
    }

    [Fact]
    public void TheCountdownIsKeptShortSoItRunsOutIfTheProgramDies()
    {
        var firmware = new Firmware { Soc = 90 };
        firmware.Run(80, polls: 30);
        Assert.InRange(firmware.MaxTimerSeen, 1, HoldChargeEngine.KeepAlive);
        Assert.All(firmware.Writes.Where(w => w.Address == HoldChargeEngine.TimerHigh), w => Assert.Equal(0, w.Target));
    }

    [Theory]
    [InlineData(0x07C3, 4u, "旧版限充仍在生效")]
    [InlineData(0x07F4, 0x11u, "不接管")]
    [InlineData(0x073F, 0x80u, "不执行保持功能")]
    public void ItRefusesBeforeWritingWhenTheStartingStateIsNotClean(int address, uint value, string expected)
    {
        var firmware = new Firmware { Soc = 90 };
        firmware.Registers[(ushort)address] = value;
        var error = Assert.Throws<InvalidOperationException>(() => firmware.Run(80, polls: 3));
        Assert.Contains(expected, error.Message);
        Assert.Empty(firmware.Writes);
        Assert.DoesNotContain(RevoHelper.App.Presentation.UiTextCatalog.Translate(error.Message, "en-US"), c => c is >= '一' and <= '鿿');
    }

    // A run that was killed leaves a short countdown behind. The next start must carry on
    // instead of refusing for half an hour.
    [Fact]
    public void AShortCountdownLeftByAKilledRunIsAdopted()
    {
        var firmware = new Firmware { Soc = 90 };
        firmware.Registers[0x07F5] = 17; firmware.Registers[0x07F6] = 90;
        var seen = firmware.Run(80, polls: 6);
        Assert.Contains(seen, o => o.Holding);
        Assert.Equal(90, firmware.Soc);
        firmware.AssertRestored();
    }

    // After a shutdown the battery was never full, so once the countdown ran out it charged to
    // 100%. A limit that is holding is therefore left applied for three days.
    [Fact]
    public void AtShutdown_AHoldingLimitIsLeftAppliedForThreeDays_AndTheNextStartTakesItBack()
    {
        var firmware = new Firmware { Soc = 90, ShuttingDown = true };
        firmware.Run(80, polls: 4);

        Assert.True(firmware.HeldForShutdown);
        Assert.Equal(0x10E0u - 0u, firmware.Registers[0x07F4] << 8 | firmware.Registers[0x07F5]);
        Assert.InRange((int)firmware.Registers[0x07F6], 90, 100);     // still at or above the charge
        // Windows may end the process within seconds: the open channel is used, high byte first.
        Assert.Equal(1, firmware.Created);
        Assert.Equal(new (ushort, byte)[] { (0x07F4, 0x10), (0x07F5, 0xE0) }, firmware.Writes.TakeLast(2));

        firmware.ShuttingDown = false;                                 // next start
        var next = firmware.Run(80, polls: 6);
        // What was left is logged before it is taken back.
        Assert.Equal(("leftover", (byte)0x10, (byte)0xE0), (next[0].Note, next[0].TimerHigh, next[0].TimerLow));
        Assert.InRange(firmware.MaxTimerSeenAfter(0x10E0), 0, HoldChargeEngine.KeepAlive);
        Assert.Equal(90, firmware.Soc);
        firmware.AssertRestored();
    }

    // If the open channel fails, one fresh channel gets one more try; if that fails too,
    // everything is put back as after an ordinary stop.
    [Fact]
    public void AtShutdown_AFailedWriteIsTriedOnceMoreOnAFreshChannel()
    {
        var firmware = new Firmware { Soc = 90, ShuttingDown = true, FailShutdownWrites = 1 };
        firmware.Run(80, polls: 4);
        Assert.True(firmware.HeldForShutdown);
        Assert.Equal(2, firmware.Created);
        Assert.Equal(0x10E0u, firmware.Registers[0x07F4] << 8 | firmware.Registers[0x07F5]);

        var failing = new Firmware { Soc = 90, ShuttingDown = true, FailShutdownWrites = 2 };
        failing.Run(80, polls: 4);
        Assert.False(failing.HeldForShutdown);
        failing.AssertRestored();
    }

    [Fact]
    public void AtShutdown_BelowTheLimitNothingIsLeftSoTheBatteryChargesNormally()
    {
        var firmware = new Firmware { Soc = 50, ShuttingDown = true };
        var seen = firmware.Run(80, polls: 3);
        Assert.False(firmware.HeldForShutdown);
        Assert.Contains(seen, o => o.Note == HoldChargeEngine.ShutdownBelowLimitNote);
        firmware.AssertRestored();
    }

    // The window waits for an answer to its shutdown signal. Below the limit the answer comes
    // before the restore, which Windows need not wait for: the countdown is already clear.
    [Fact]
    public void AtShutdown_BelowTheLimit_TheAnswerComesBeforeTheRestore()
    {
        var firmware = new Firmware { Soc = 85, ShuttingDown = true };
        firmware.AfterPoll = poll => { if (poll == 2) firmware.Soc = 70; };
        var seen = firmware.Run(80, polls: 4);

        Assert.False(firmware.HeldForShutdown);
        var answer = seen.FindIndex(o => o.Note == HoldChargeEngine.ShutdownBelowLimitNote);
        Assert.InRange(answer, 0, seen.FindIndex(o => o.Note == "restored") - 1);
        Assert.Equal((byte)0, seen[answer - 1].TimerLow);
        Assert.DoesNotContain(seen, o => o.Note.StartsWith("shutdown-hold", StringComparison.Ordinal));
        firmware.AssertRestored();
    }

    // The high byte is written but the low one is not, and the restore fails too: almost three
    // days stay on the countdown. Then nothing may report the limit as put back, nor promise that
    // it runs out within half an hour (see HoldChargeHost.StoppedUnconfirmed).
    [Fact]
    public void AtShutdown_APartlyWrittenHoldThatCannotBeTakenBack_IsNotReportedAsPutBack()
    {
        var firmware = new Firmware
        {
            Soc = 90, ShuttingDown = true,
            FailWrite = (address, target) => (address == 0x07F5 && target == 0xE0) || (address is 0x07F4 or 0x07F5 && target == 0),
        };
        var seen = firmware.Run(80, polls: 4);

        Assert.False(firmware.HeldForShutdown);
        Assert.False(firmware.Restored);
        Assert.Equal(0x10u, firmware.Registers[0x07F4]);
        Assert.Equal(2, seen.Count(o => o.Note.StartsWith("shutdown-hold failed", StringComparison.Ordinal)));
        Assert.DoesNotContain(seen, o => o.Note == "restored");
    }

    // A long countdown left by a shutdown is taken back even when a read fails on the way.
    // Otherwise it would keep the battery from charging for days with nothing looking after it.
    [Theory]
    [InlineData((ushort)0x07F6)]
    [InlineData((ushort)0x0984)]
    public void AFailedReadDoesNotStopTheTakeoverOfALongCountdown(ushort address)
    {
        var firmware = new Firmware { Soc = 90, FailFirstReadOf = address };
        firmware.Registers[0x07F4] = 0x10; firmware.Registers[0x07F5] = 0xD0; firmware.Registers[0x07F6] = 90;
        var seen = firmware.Run(80, polls: 6);

        Assert.Null(firmware.FailFirstReadOf);                          // the read did fail once
        Assert.Equal(("leftover", (byte)0x10, (byte)0xD0), (seen[0].Note, seen[0].TimerHigh, seen[0].TimerLow));
        Assert.Contains(seen, o => o.Note.StartsWith("retry: ", StringComparison.Ordinal));
        Assert.Contains(seen, o => o.Holding);
        Assert.InRange(firmware.MaxTimerSeenAfter(0x10D0), 0, HoldChargeEngine.KeepAlive);
        Assert.Equal(90, firmware.Soc);
        Assert.True(firmware.Restored);
        firmware.AssertRestored();
    }

    [Fact]
    public void UnpluggingStopsAndPutsEverythingBack()
    {
        var firmware = new Firmware { Soc = 90 };
        firmware.AfterPoll = poll => { if (poll == 3) firmware.Online = false; };
        firmware.Run(80, polls: 10);
        Assert.Contains("电源", firmware.StopReason);
        firmware.AssertRestored();
    }

    [Fact]
    public void ALostRaceWithTheCountdownIsRetriedOnAFreshChannel()
    {
        var firmware = new Firmware { Soc = 90, FailTimerWrites = 2 };
        firmware.Run(80, polls: 8);
        Assert.True(firmware.Created > 2);
        Assert.InRange(firmware.Soc, 90, 92);                // it may charge a little while the write is retried
        Assert.Equal(0, firmware.ForcedDischarges);
        firmware.AssertRestored();
    }

    // The user's stop and Windows' question can reach the worker in either order while a round is
    // under way. A stop that came first goes ahead and everything is put back; a stop after the
    // shutdown signal (the program exiting for the shutdown) still leaves the limit applied.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AStopAndAShutdownInEitherOrder(bool stopFirst)
    {
        // Unnamed: a running RevoHelper on this machine is never signalled by a test.
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset);
        using var shutdown = new EventWaitHandle(false, EventResetMode.ManualReset);
        var firmware = new Firmware { Soc = 90 };
        firmware.AfterPoll = poll =>
        {
            if (poll != 3) return;
            if (stopFirst) stop.Set();
            HoldChargeHost.SignalShutdownUnlessStopped(stop, shutdown);
            if (!stopFirst) stop.Set();
            firmware.ShuttingDown = shutdown.WaitOne(0);
        };

        firmware.Run(80, polls: 3);

        Assert.Equal(!stopFirst, firmware.HeldForShutdown);
        if (stopFirst) firmware.AssertRestored();
    }

    private sealed class Firmware
    {
        public readonly Dictionary<ushort, uint> Registers = new()
        {
            [0x0742] = 0x22, [0x0770] = 0xFF, [0x07C3] = 0x3807, [0x07A6] = 0x28, [0x07B9] = 0, [0x07D0] = 0,
            [0x0490] = 0x0F, [0x0497] = 0, [0x04AB] = 0, [0x9164] = 0,
            [0x07F4] = 0, [0x07F5] = 0, [0x07F6] = 0, [0x0984] = 0, [0x06E6] = 0, [0x073F] = 0, [0x0727] = 0, [0x097C] = 0
        };
        public readonly List<(ushort Address, byte Target)> Writes = [];
        public readonly List<int> Timers = [];
        /// <summary>Largest countdown seen once it has come back down from the given value.</summary>
        public int MaxTimerSeenAfter(int from) { var i = Timers.FindLastIndex(t => t > HoldChargeEngine.KeepAlive); return Timers.Skip(i + 1).DefaultIfEmpty(0).Max(); }
        public int Soc, ForcedDischarges, Created, FailTimerWrites, FailShutdownWrites, MaxTimerSeen;
        public bool Online = true, Charging = true;
        public Action<int>? AfterPoll;
        public string? StopReason;
        public bool ShuttingDown, HeldForShutdown, Restored;
        /// <summary>The next read of this address fails and leaves its channel unusable.</summary>
        public ushort? FailFirstReadOf;
        /// <summary>Writes it returns true for fail and leave their channel unusable.</summary>
        public Func<ushort, byte, bool>? FailWrite;

        public List<HoldChargeObservation> Run(int limit, int polls)
        {
            var seen = new List<HoldChargeObservation>();
            var poll = 0;
            var engine = new HoldChargeEngine(() => new Transport(this, ++Created));
            Restored = engine.Run(limit, () => poll >= polls, () => { poll++; Step(); AfterPoll?.Invoke(poll); }, seen.Add, () => ShuttingDown);
            HeldForShutdown = engine.HeldForShutdown;
            StopReason = engine.StopReason;
            return seen;
        }

        private void Step()
        {
            var timer = (int)(Registers[0x07F4] << 8 | Registers[0x07F5]);
            MaxTimerSeen = Math.Max(MaxTimerSeen, timer);
            Timers.Add(timer);
            if (timer != 0)
            {
                if (Soc <= (int)Registers[0x07F6]) { Charging = false; Registers[0x0984] = 8; }
                else { ForcedDischarges++; Soc--; }
                timer--;
                Registers[0x07F4] = (uint)(timer >> 8); Registers[0x07F5] = (uint)(timer & 0xFF);
            }
            else
            {
                Registers[0x0984] = 0; Charging = Soc < 100;
                if (Charging) Soc++;
            }
        }

        public void AssertRestored()
        {
            Assert.Equal(0u, Registers[0x07F4]); Assert.Equal(0u, Registers[0x07F5]); Assert.Equal(0u, Registers[0x07F6]);
            Assert.Equal(7, (byte)Registers[0x07C3]);
            Assert.All(Writes, w => Assert.Contains(w.Address, new ushort[] { 0x07F4, 0x07F5, 0x07F6 }));
        }

        private sealed class Transport(Firmware owner, int id) : IReviewedChargeTransport
        {
            private bool broken;
            public void VerifySessionOrigin(ChargeEcSnapshot original) { }
            public ChargeEcSnapshot ReadSnapshot()
            {
                Check();
                owner.Registers[0x04AB] = (uint)owner.Soc;
                return new(new("", "", "", -1, -1), new Dictionary<ushort, uint>(owner.Registers), owner.Soc, owner.Online,
                    owner.Charging, false, owner.Charging ? 30000u : 0u, 90000, DateTimeOffset.UtcNow, ReviewedChargeProtocol.LogicalZeroInstance);
            }
            public uint ReadByte(ushort address)
            {
                Check(); ReviewedChargeProtocol.EncodeRead(address);
                if (owner.FailFirstReadOf == address) { owner.FailFirstReadOf = null; broken = true; throw new InvalidDataException("read failed"); }
                return owner.Registers[address];
            }
            public void WriteOwnedByte(ushort address, byte expected, byte target)
            {
                Check(); ReviewedChargeProtocol.EncodeOwnedWrite(address, expected, target);
                if ((byte)owner.Registers[address] != expected) throw new InvalidOperationException("changed externally");
                if (address == 0x07F5 && target != 0 && owner.FailTimerWrites > 0)
                { owner.FailTimerWrites--; broken = true; throw new InvalidDataException("readback did not match"); }
                if (address == 0x07F4 && target != 0 && owner.FailShutdownWrites > 0)
                { owner.FailShutdownWrites--; broken = true; throw new InvalidDataException("readback did not match"); }
                if (owner.FailWrite?.Invoke(address, target) == true)
                { broken = true; throw new InvalidDataException("write failed"); }
                owner.Writes.Add((address, target));
                owner.Registers[address] = target;
            }
            private void Check() { if (broken) throw new InvalidOperationException("quarantined " + id); }
            public void Dispose() { }
        }
    }
}
