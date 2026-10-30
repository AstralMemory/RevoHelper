using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.App.Charging;

public sealed record ChargeRecoveryResult(bool PlatformRestored, bool ThresholdRestored,
    bool CacheMatchesOriginal, string[] Errors)
{
    public bool CriticalRestored => PlatformRestored && ThresholdRestored;
}

/// <summary>Runs only in an explicit worker process, never on the UI thread.</summary>
public sealed class ChargeSessionEngine(Func<IReviewedChargeTransport> createTransport)
{
    public static void ValidateInitial(ChargeEcSnapshot s)
    {
        foreach (var pair in new (ushort Address, byte Value)[] { (0x07C3, 7), (0x0770, 255), (0x07B9, 0), (0x07D0, 0), (0x9164, 0) })
            if (s.Byte(pair.Address) != pair.Value) throw new InvalidOperationException($"原始状态0x{pair.Address:X4}已改变；不接管已有设置。");
        if (s.Registers[0x07B9] > 255 || (s.Byte(0x07A6) & 0x30) != 0x20 || (s.Byte(0x0742) & 4) != 0)
            throw new InvalidOperationException("请先恢复原生工作站养护档及原始阈值；不强制接管。");
        if (!s.PowerOnline || (s.Byte(0x0490) & 5) != 5)
            throw new InvalidOperationException("请连接电源并确认电池状态有效后，再手动启用。");
    }

    public static void ValidateActive(ChargeEcSnapshot s, ChargeEcSnapshot original, int limit)
    {
        if (!string.Equals(ReviewedChargeProtocol.SessionInstance(s), ReviewedChargeProtocol.SessionInstance(original), StringComparison.OrdinalIgnoreCase)
            || s.Byte(0x07C3) != 4 || s.Byte(0x0770) != 255
            || s.Byte(0x07D0) != 0 || (s.Byte(0x07A6) & 0x30) != 0x20
            || (s.Byte(0x0742) & 0xFB) != (original.Byte(0x0742) & 0xFB)
            || (s.Byte(0x07B9) & 0x7F) != limit || s.Byte(0x9164) is var cache && cache != 0 && cache != limit)
            throw new InvalidOperationException("平台或阈值被外部改变；停止实验，不反复覆盖。");
        if (!s.PowerOnline || (s.Byte(0x0490) & 5) != 5)
            throw new InvalidOperationException("已断开电源或电池状态无效，尝试恢复原值。");
    }

    public ChargeRecoveryResult Run(ChargeEcSnapshot original, int limit,
        Func<bool> shouldStop, Action checkOwners, Action wait,
        Action<string, ushort?, int?, int?> journal, Action<ChargeEcSnapshot> publish,
        Action? recoveryWait = null)
    {
        if (limit is not (60 or 80)) throw new ArgumentOutOfRangeException(nameof(limit));
        bool thresholdAttempted = false, platformAttempted = false;
        ChargeRecoveryResult? recovery = null;
        try
        {
            using var transport = createTransport();
            transport.VerifySessionOrigin(original);
            var fresh = transport.ReadSnapshot();
            ValidateInitial(fresh);
            if ((fresh.Byte(0x0742) & 0xFB) != (original.Byte(0x0742) & 0xFB))
                throw new InvalidOperationException("相关状态在启用前改变；不写入。");
            checkOwners();
            if (shouldStop()) return new(true, true, true, []);
            journal("ThresholdApplyIntent", 0x07B9, 0, limit);
            thresholdAttempted = true;
            SetThreshold(transport, 0, (byte)limit);
            checkOwners();
            if (shouldStop()) throw new OperationCanceledException("启用期间收到停止请求。");
            journal("PlatformApplyIntent", 0x07C3, 7, 4);
            platformAttempted = true;
            transport.WriteOwnedByte(0x07C3, 7, 4);
            var lastTick = Environment.TickCount64;
            var gateFailures = 0;
            while (!shouldStop())
            {
                checkOwners();
                if (Environment.TickCount64 - lastTick > 10000)
                    throw new InvalidOperationException("监测中断或系统刚恢复，停止实验并恢复原值。");
                var current = transport.ReadSnapshot();
                if (Environment.TickCount64 - lastTick > 10000)
                    throw new InvalidOperationException("硬件观察耗时过长，停止实验并恢复原值。");
                ValidateActive(current, original, limit);
                if ((current.Byte(0x0742) & 4) == 0 && ++gateFailures >= 3)
                    throw new InvalidOperationException("固件限充门控未开启，停止实验。");
                if ((current.Byte(0x0742) & 4) != 0) gateFailures = 0;
                publish(current);
                if (Environment.TickCount64 - lastTick > 10000)
                    throw new InvalidOperationException("状态发布耗时过长，停止实验并恢复原值。");
                lastTick = Environment.TickCount64;
                wait();
            }
        }
        finally
        {
            if (thresholdAttempted || platformAttempted)
                recovery = Recover(original, limit, thresholdAttempted, platformAttempted, false, journal, recoveryWait ?? wait);
        }
        return recovery ?? new(true, true, true, []);
    }

    public ChargeRecoveryResult Recover(ChargeEcSnapshot original, int limit, bool thresholdAttempted,
        bool platformAttempted, bool emergency, Action<string, ushort?, int?, int?> journal, Action wait)
    {
        var errors = new List<string>();
        if (emergency) RestorePlatform();
        try
        {
            // Fresh channels ensure a failed B9 operation cannot quarantine C3 recovery.
            using var transport = OpenRecoveryTransport();
            var raw = transport.ReadByte(0x07B9);
            if (raw > 255) throw new InvalidOperationException("阈值读取失败或格式改变。");
            var value = (byte)(raw & 0x7F);
            if (thresholdAttempted && value == limit)
            {
                TryJournal("ThresholdRestoreIntent", 0x07B9, limit, 0);
                SetThreshold(transport, (byte)limit, 0);
            }
            else if (value != 0) throw new InvalidOperationException("阈值由其他程序改变，不覆盖。");
            if (!emergency && platformAttempted)
                for (var i = 0; i < 2; i++)
                {
                    wait();
                    using var cache = OpenRecoveryTransport();
                    if (ReviewedChargeProtocol.DecodeByte(cache.ReadByte(0x9164)) == 0) break;
                }
        }
        catch (Exception ex) { errors.Add("阈值恢复：" + ex.Message); }
        finally { if (!emergency) RestorePlatform(); }
        var platform = Verify(0x07C3, value => value == 7);
        var threshold = Verify(0x07B9, value => (value & 0x7F) == 0);
        var cacheMatches = Verify(0x9164, value => value == original.Byte(0x9164));
        return new(platform, threshold, cacheMatches, [.. errors]);

        void RestorePlatform()
        {
            try
            {
                using var transport = OpenRecoveryTransport();
                var value = ReviewedChargeProtocol.DecodeByte(transport.ReadByte(0x07C3));
                if (platformAttempted && value == 4)
                {
                    TryJournal("PlatformRestoreIntent", 0x07C3, 4, 7);
                    transport.WriteOwnedByte(0x07C3, 4, 7);
                }
                else if (value != 7) throw new InvalidOperationException("平台状态由其他程序改变，不覆盖。");
            }
            catch (Exception ex) { errors.Add("平台恢复：" + ex.Message); }
        }
        bool Verify(ushort address, Func<byte, bool> check)
        {
            try
            {
                using var transport = OpenRecoveryTransport();
                var raw = transport.ReadByte(address);
                return (address != 0x07B9 || raw <= 255) && check(ReviewedChargeProtocol.DecodeByte(raw));
            }
            catch (Exception ex) { errors.Add($"复核0x{address:X4}：{ex.Message}"); return false; }
        }
        void TryJournal(string phase, ushort address, int from, int to)
        { try { journal(phase, address, from, to); } catch (Exception) { /* Never prevent restoration. */ } }

        IReviewedChargeTransport OpenRecoveryTransport()
        {
            var transport = createTransport();
            try
            {
                // Bind the saved route, not a laptop label or firmware-version guess.
                // This does not depend on full battery telemetry, so a failed B9 read
                // still cannot prevent a fresh, independently owned C3 restoration.
                transport.VerifySessionOrigin(original);
                return transport;
            }
            catch { transport.Dispose(); throw; }
        }
    }

    private static void SetThreshold(IReviewedChargeTransport transport, byte expectedLow, byte targetLow)
    {
        var raw = transport.ReadByte(0x07B9);
        if (raw > 255 || (raw & 0x7F) != expectedLow) throw new InvalidOperationException("阈值所有权已改变，不覆盖。");
        var expected = (byte)raw;
        transport.WriteOwnedByte(0x07B9, expected, (byte)((expected & 0x80) | targetLow));
    }
}
