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

        firmware.ShuttingDown = false;                                 // next start
        firmware.Run(80, polls: 6);
        Assert.InRange(firmware.MaxTimerSeenAfter(0x10E0), 0, HoldChargeEngine.KeepAlive);
        Assert.Equal(90, firmware.Soc);
        firmware.AssertRestored();
    }

    [Fact]
    public void AtShutdown_BelowTheLimitNothingIsLeftSoTheBatteryChargesNormally()
    {
        var firmware = new Firmware { Soc = 50, ShuttingDown = true };
        firmware.Run(80, polls: 3);
        Assert.False(firmware.HeldForShutdown);
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
        public int Soc, ForcedDischarges, Created, FailTimerWrites, MaxTimerSeen;
        public bool Online = true, Charging = true;
        public Action<int>? AfterPoll;
        public string? StopReason;
        public bool ShuttingDown, HeldForShutdown;

        public List<HoldChargeObservation> Run(int limit, int polls)
        {
            var seen = new List<HoldChargeObservation>();
            var poll = 0;
            var engine = new HoldChargeEngine(() => new Transport(this, ++Created));
            engine.Run(limit, () => poll >= polls, () => { poll++; Step(); AfterPoll?.Invoke(poll); }, seen.Add, () => ShuttingDown);
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
            public uint ReadByte(ushort address) { Check(); ReviewedChargeProtocol.EncodeRead(address); return owner.Registers[address]; }
            public void WriteOwnedByte(ushort address, byte expected, byte target)
            {
                Check(); ReviewedChargeProtocol.EncodeOwnedWrite(address, expected, target);
                if ((byte)owner.Registers[address] != expected) throw new InvalidOperationException("changed externally");
                if (address == 0x07F5 && target != 0 && owner.FailTimerWrites > 0)
                { owner.FailTimerWrites--; broken = true; throw new InvalidDataException("readback did not match"); }
                owner.Writes.Add((address, target));
                owner.Registers[address] = target;
            }
            private void Check() { if (broken) throw new InvalidOperationException("quarantined " + id); }
            public void Dispose() { }
        }
    }
}
