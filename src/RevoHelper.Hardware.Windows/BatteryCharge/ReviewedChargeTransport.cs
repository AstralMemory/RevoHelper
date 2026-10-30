using System.Collections.ObjectModel;

namespace RevoHelper.Hardware.Windows.BatteryCharge;

/// <summary>
/// Explicitly invoked, serialized transport for the narrow charge-control interface.
/// Construction performs no WMI, native calls, reads or writes. This class never
/// enables a session, holds an override, retries, or touches persistent storage.
/// </summary>
public sealed class ReviewedChargeTransport : IReviewedChargeTransport
{
    private readonly object gate = new();
    private readonly Action<TimeSpan> throttle;
    private readonly TimeSpan timeout;
    private IReviewedChargeBackend? backend;
    // Kept only for compatibility with serialized leases from older versions.
    // Charge control does not query SMBIOS or use these labels as authorization.
    private static readonly ChargeMachineIdentity UnspecifiedIdentity = new("", "", "", -1, -1);
    private string? instanceName;
    private bool initialized;
    private bool disposed;
    private volatile bool timedOut;
    private bool uncertainWrite;
    private bool originRejected;

    public ReviewedChargeTransport() : this(null, null, null) { }

    public ReviewedChargeTransport(IReviewedChargeBackend? backend, Action<TimeSpan>? throttle = null,
        TimeSpan? operationTimeout = null)
    {
        this.backend = backend;
        this.throttle = throttle ?? Thread.Sleep;
        timeout = operationTimeout ?? ReviewedChargeProtocol.OperationTimeout;
        if (timeout <= TimeSpan.Zero || timeout > ReviewedChargeProtocol.OperationTimeout)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
    }

    public ChargeEcSnapshot ReadSnapshot()
    {
        lock (gate)
        {
            CheckUsable();
            EnsureInitialized();
            try
            {
                var registers = new Dictionary<ushort, uint>();
                foreach (var address in ReviewedChargeProtocol.SnapshotAddresses)
                    registers.Add(address, ReadRaw(address));
                var telemetry = InvokeBounded(() => backend!.ReadBatteryTelemetry(timeout));
                ReviewedChargeProtocol.ValidateTelemetry(telemetry);
                return new(UnspecifiedIdentity, new ReadOnlyDictionary<ushort, uint>(registers), telemetry.WindowsPercent,
                    telemetry.PowerOnline, telemetry.Charging, telemetry.Discharging, telemetry.ChargeRateMw,
                    telemetry.RemainingCapacity, DateTimeOffset.UtcNow, instanceName);
            }
            catch { initialized = false; throw; }
        }
    }

    public void VerifySessionOrigin(ChargeEcSnapshot original)
    {
        lock (gate)
        {
            CheckUsable();
            EnsureInitialized();
            if (!string.Equals(instanceName, ReviewedChargeProtocol.SessionInstance(original), StringComparison.OrdinalIgnoreCase))
            {
                originRejected = true;
                initialized = false;
                throw new InvalidDataException("The selected WMI route differs from this session; no control calls were issued.");
            }
        }
    }

    public uint ReadByte(ushort address)
    {
        ReviewedChargeProtocol.EncodeRead(address); // Reject arbitrary addresses before any initialization I/O.
        lock (gate)
        {
            CheckUsable(); EnsureInitialized();
            return ReadRaw(address);
        }
    }

    public void WriteOwnedByte(ushort address, byte expected, byte target)
    {
        var packet = ReviewedChargeProtocol.EncodeOwnedWrite(address, expected, target);
        lock (gate)
        {
            CheckUsable();
            if (!initialized) throw new InvalidOperationException("An explicit reviewed read must initialize the transport before any write.");
            var raw = ReadRaw(address);
            if (address == 0x07B9 && raw > byte.MaxValue)
                throw new InvalidDataException("A battery-threshold write requires an unambiguous one-byte raw return.");
            if (ReviewedChargeProtocol.DecodeByte(raw) != expected)
                throw new InvalidOperationException("The EC byte changed externally; the owned write was not issued.");
            try
            {
                InvokePacket(packet);
                var observed = ReviewedChargeProtocol.DecodeByte(ReadRaw(address));
                // Bit 7 is firmware-owned and can change as soon as the battery reaches
                // the target. Only the threshold bits are our post-write readback.
                if (address == 0x07B9 ? (observed & 0x7F) != (target & 0x7F) : observed != target)
                    throw new InvalidDataException("EC write readback did not match; no retry was attempted.");
            }
            catch
            {
                // A missing/failed acknowledgement is not proof that the write did
                // not occur. Further operations require a new, independently gated
                // transport so recovery does not continue on an uncertain channel.
                uncertainWrite = true;
                throw;
            }
        }
    }

    private void EnsureInitialized()
    {
        if (initialized) return;
        backend ??= new SystemManagementReviewedChargeBackend();
        if (!InvokeBounded(backend.IsAdministrator))
            throw new UnauthorizedAccessException("Explicit administrator launch is required; automatic elevation is not attempted.");
        var signature = InvokeBounded(() => backend.ReadInterfaceSignature(timeout));
        if (!ReviewedChargeProtocol.WmiSignatureMatches(signature))
            throw new InvalidDataException("WMI GUID/method/signature does not match the reviewed protocol.");
        instanceName = ReviewedChargeProtocol.SelectReviewedInstance(InvokeBounded(() => backend.ReadInterfaceInstances(timeout)));
        initialized = true;
    }

    private uint ReadRaw(ushort address) => InvokePacket(ReviewedChargeProtocol.EncodeRead(address));

    private uint InvokePacket(ulong packet)
    {
        try
        {
            var result = InvokeBounded(() => backend!.InvokeGetSetULong(instanceName!, packet, timeout));
            if (result is not uint raw) throw new InvalidDataException("EC method did not return a UInt32 Return value.");
            ReviewedChargeProtocol.DecodeByte(raw); // Check the full sentinel before anybody masks it.
            return raw;
        }
        finally { throttle(ReviewedChargeProtocol.EcCallSpacing); }
    }

    private T InvokeBounded<T>(Func<T> operation)
    {
        CheckUsable();
        var task = Task.Run(() =>
        {
            // A queued task may not start after its caller's deadline has expired.
            if (timedOut) throw new InvalidOperationException("Transport deadline expired before operation start.");
            return operation();
        });
        try { return task.WaitAsync(timeout).GetAwaiter().GetResult(); }
        catch (TimeoutException)
        {
            timedOut = true;
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            // A deadline is NOT proof that Windows/firmware cancelled an in-flight
            // call. Quarantine this instance; an independent monitor owns recovery.
            throw new TimeoutException("WMI operation exceeded its deadline. This transport is quarantined; an in-flight native call may still complete.");
        }
    }

    private void CheckUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (timedOut) throw new InvalidOperationException("Transport is quarantined after a deadline; no further calls are issued.");
        if (uncertainWrite) throw new InvalidOperationException("Transport is quarantined after an unconfirmed write; independent recovery is required.");
        if (originRejected) throw new InvalidOperationException("Transport belongs to a different session route; no further calls are issued.");
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            backend?.Dispose();
        }
    }
}
