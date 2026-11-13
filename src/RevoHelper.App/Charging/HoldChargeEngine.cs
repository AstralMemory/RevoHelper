using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Charging;

public sealed record HoldChargeObservation(DateTimeOffset At, int Soc, bool Holding, byte Threshold, byte TimerHigh,
    byte TimerLow, byte Flags, bool PowerOnline, bool Charging, uint ChargeRateMw, string Note);

/// <summary>
/// The charge limiter. It leaves the platform state alone. The firmware has a timed "hold"
/// feature (EC image bank2:B969): while the 16-bit countdown at 07F4:07F5 is non-zero it stops
/// charging whenever the charge is at or below the percentage in 07F6, and it force-discharges
/// when the charge is above it. This engine only ever asks for the first half: the threshold is
/// kept at or above the current charge, the countdown is kept short and refreshed, and below the
/// user's limit the countdown is cleared so the battery charges normally. If this process dies,
/// the countdown runs out on its own.
/// </summary>
public sealed class HoldChargeEngine(Func<IReviewedChargeTransport> createTransport)
{
    public const ushort TimerHigh = 0x07F4, TimerLow = 0x07F5, Threshold = 0x07F6, Flags = 0x0984;
    public const byte KeepAlive = 30, RefreshBelow = 20;
    // 0x10E0 minutes = 3 days: how long a limit that was holding stays applied after Windows shuts down.
    public const byte ShutdownHigh = 0x10, ShutdownLow = 0xE0;
    public const int ResumeBelowLimitBy = 3;

    public static void ValidateInitial(ChargeEcSnapshot s, byte timerHigh, byte timerLow, byte gate073F, byte gate0727, byte gate097C)
    {
        if (!s.PowerOnline || (s.Byte(0x0490) & 5) != 5)
            throw new InvalidOperationException("请连接电源并确认电池状态有效后，再手动启用。");
        if (s.Byte(0x07C3) != 7 || s.Byte(0x0770) != 255)
            throw new InvalidOperationException("旧版限充仍在生效，请先恢复原值。");
        // A countdown up to the shutdown length is our own, left by a shutdown or by a run that
        // was killed: carry on from it. Anything longer was set by something else.
        if (timerHigh > ShutdownHigh)
            throw new InvalidOperationException($"固件的保持计时已在使用（0x{timerHigh:X2}{timerLow:X2}）；不接管。");
        if ((gate073F & 0x80) != 0 || (gate0727 & 1) != 0 || (gate097C & 1) != 0)
            throw new InvalidOperationException($"固件当前不执行保持功能（073F=0x{gate073F:X2} 0727=0x{gate0727:X2} 097C=0x{gate097C:X2}）。");
    }

    /// <returns>True when everything this engine wrote was put back.</returns>
    public bool Run(int limit, Func<bool> shouldStop, Action wait, Action<HoldChargeObservation> observe,
        Func<bool>? keepForShutdown = null)
    {
        if (limit is not (60 or 80)) throw new ArgumentOutOfRangeException(nameof(limit));
        var transport = createTransport();
        byte originalThreshold = 0;
        var touched = false;
        var holding = false;
        StopReason = null;
        HeldForShutdown = false;
        try
        {
            var first = transport.ReadSnapshot();
            var leftoverHigh = Read(transport, TimerHigh);
            var leftoverLow = Read(transport, TimerLow);
            var leftover = leftoverHigh != 0 || leftoverLow != 0;
            // After an earlier run the threshold still holds our last value; the firmware's own is 0.
            originalThreshold = leftover ? (byte)0 : Read(transport, Threshold);
            ValidateInitial(first, leftoverHigh, leftoverLow,
                Read(transport, 0x073F), Read(transport, 0x0727), Read(transport, 0x097C));
            // Only after the checks: a refusal must not write anything, not even a restore.
            touched = leftover;
            var failures = 0;
            while (!shouldStop())
            {
                string note;
                try
                {
                    var s = transport.ReadSnapshot();
                    if (!s.PowerOnline || (s.Byte(0x0490) & 5) != 5)
                        throw new HoldStopException("已断开电源或电池状态无效，停止并恢复。");
                    if (s.Byte(0x07C3) != 7) throw new HoldStopException("平台状态被改变，停止并恢复。");
                    var soc = s.Byte(0x04AB);
                    var high = Read(transport, TimerHigh);
                    var low = Read(transport, TimerLow);
                    var threshold = Read(transport, Threshold);
                    // A long countdown is the one left for a shutdown: back to the short, renewed one.
                    if (high != 0) { touched = true; transport.WriteOwnedByte(TimerHigh, high, 0); high = 0; }
                    var want = holding ? soc > limit - ResumeBelowLimitBy : soc >= limit;
                    if (want)
                    {
                        // Never below the current charge: above the threshold the firmware discharges.
                        // One point of headroom covers a gauge reading that ticks up between polls.
                        var target = (byte)Math.Min(100, Math.Max(limit, soc + 1));
                        if (threshold != target) { touched = true; transport.WriteOwnedByte(Threshold, threshold, target); threshold = target; }
                        if (low < RefreshBelow || low > KeepAlive) { touched = true; transport.WriteOwnedByte(TimerLow, low, KeepAlive); low = KeepAlive; }
                        note = holding ? "hold" : "hold-start";
                        holding = true;
                    }
                    else
                    {
                        if (low != 0) { transport.WriteOwnedByte(TimerLow, low, 0); low = 0; }
                        note = holding ? "release" : "charge";
                        holding = false;
                    }
                    observe(new(DateTimeOffset.Now, soc, holding, threshold, high, low, Read(transport, Flags),
                        s.PowerOnline, s.Charging, s.ChargeRateMw, note));
                    failures = 0;
                }
                catch (HoldStopException) { throw; }
                catch (Exception exception) when (++failures < 5)
                {
                    // The countdown ticks under us, so an owned write can lose a race. A failed
                    // write may leave the channel unusable: start a fresh one and look again.
                    observe(new(DateTimeOffset.Now, -1, holding, 0, 0, 0, 0, true, false, 0, "retry: " + exception.Message));
                    transport.Dispose();
                    transport = createTransport();
                }
                wait();
            }
        }
        catch (HoldStopException exception) { StopReason = exception.Message; }
        catch (Exception exception) when (touched) { StopReason = exception.Message; }
        finally
        {
            transport.Dispose();
        }
        if (StopReason is null && holding && keepForShutdown?.Invoke() == true && ExtendForShutdown(observe))
        {
            HeldForShutdown = true;
            return true;
        }
        return !touched || Release(originalThreshold, observe);
    }

    /// <summary>The last run ended with the limit left applied for a Windows shutdown.</summary>
    public bool HeldForShutdown { get; private set; }

    // Windows is shutting down while the limit is holding. Without this the countdown runs out
    // half an hour later and the battery, which was never full, starts charging to 100%.
    private bool ExtendForShutdown(Action<HoldChargeObservation> observe)
    {
        try
        {
            using var transport = createTransport();
            transport.ReadSnapshot();
            transport.WriteOwnedByte(TimerLow, Read(transport, TimerLow), ShutdownLow);
            transport.WriteOwnedByte(TimerHigh, Read(transport, TimerHigh), ShutdownHigh);
            observe(new(DateTimeOffset.Now, -1, true, Read(transport, Threshold), ShutdownHigh, ShutdownLow,
                Read(transport, Flags), true, false, 0, "shutdown-hold"));
            return true;
        }
        catch (Exception exception)
        {
            observe(new(DateTimeOffset.Now, -1, true, 0, 0, 0, 0, true, false, 0, "shutdown-hold failed: " + exception.Message));
            return false;
        }
    }

    /// <summary>Why the last run ended on its own; null after an ordinary stop request.</summary>
    public string? StopReason { get; private set; }

    /// <summary>Clears the countdown and puts the threshold back. Safe to call when nothing was written.</summary>
    public bool Release(byte originalThreshold, Action<HoldChargeObservation>? observe = null)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                using var transport = createTransport();
                transport.ReadSnapshot();
                var high = Read(transport, TimerHigh);
                if (high != 0) transport.WriteOwnedByte(TimerHigh, high, 0);
                var low = Read(transport, TimerLow);
                if (low != 0) transport.WriteOwnedByte(TimerLow, low, 0);
                var threshold = Read(transport, Threshold);
                if (threshold != originalThreshold && originalThreshold <= 100)
                    transport.WriteOwnedByte(Threshold, threshold, originalThreshold);
                if (Read(transport, TimerHigh) == 0 && Read(transport, TimerLow) == 0)
                {
                    observe?.Invoke(new(DateTimeOffset.Now, -1, false, Read(transport, Threshold), 0, 0, Read(transport, Flags), true, false, 0, "restored"));
                    return true;
                }
            }
            catch (Exception exception)
            {
                observe?.Invoke(new(DateTimeOffset.Now, -1, false, 0, 0, 0, 0, true, false, 0, "restore-retry: " + exception.Message));
            }
        }
        return false;
    }

    // The interface may return neighbouring bytes above the low one; only the low byte is ours.
    private static byte Read(IReviewedChargeTransport transport, ushort address) =>
        ReviewedChargeProtocol.DecodeByte(transport.ReadByte(address));
}

public sealed class HoldStopException(string message) : Exception(message);
