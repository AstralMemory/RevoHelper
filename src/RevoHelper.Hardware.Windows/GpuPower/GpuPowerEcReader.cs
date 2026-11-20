using System.Collections.ObjectModel;
using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.Hardware.Windows.GpuPower;

/// <summary>EC bytes behind the discrete GPU's power limit, as read. Missing addresses were not readable.</summary>
public sealed record GpuPowerEcReadout(
    IReadOnlyDictionary<ushort, byte> Bytes,
    IReadOnlyList<ushort> Unread,
    int BusyRetries,
    string? Failure)
{
    public byte? this[ushort address] => Bytes.TryGetValue(address, out var value) ? value : null;

    public static GpuPowerEcReadout Unavailable(string reason) =>
        new(new Dictionary<ushort, byte>(), GpuPowerEcReader.Addresses, 0, reason);
}

/// <summary>
/// Reads the EC bytes that set the discrete GPU's power limit (firmware study of 2026-11-14/16).
/// It goes through the same WMI route and the same checks as the charge limiter, reads a fixed
/// list of addresses and has no write path at all. The charge limiter can be polling the same
/// firmware mailbox from its own process; the firmware then answers the full failure sentinel,
/// which is read again after a short pause. Nothing here runs until <see cref="Read"/> is called.
/// </summary>
public sealed class GpuPowerEcReader
{
    public const ushort TableStart = 0x9190;
    public const int TableRows = 6, RowStride = 0x10, RowBytes = 8;
    public const int MaxAttempts = 4;
    public static readonly TimeSpan CallSpacing = TimeSpan.FromMilliseconds(15);
    public static readonly TimeSpan BusyPause = TimeSpan.FromMilliseconds(40);

    // Gates, the table selector and row offset, the GPU id, the host's custom inputs, then the
    // values the firmware passes on (source / ACPI mirror pairs) and the row's other fields.
    public static IReadOnlyList<ushort> StateAddresses { get; } = Array.AsReadOnly<ushort>(
    [
        0x0490, 0x0456, 0x0741, 0x0727, 0x072B, 0x07C4, 0x07C7, 0x07C8, 0x0A6E, 0x07D2,
        0x0743, 0x0744, 0x0745, 0x0746, 0x0747,
        0x09EA, 0x0788, 0x09EB, 0x07D4, 0x09EC, 0x07D5, 0x07F7, 0x9062, 0x0763, 0x0764, 0x07D6, 0x07D7
    ]);

    /// <summary>Two tables of three rows; the first eight bytes of each (seven are known fields).</summary>
    public static IReadOnlyList<ushort> TableAddresses { get; } = Array.AsReadOnly(
        Enumerable.Range(0, TableRows)
            .SelectMany(row => Enumerable.Range(0, RowBytes).Select(column => (ushort)(TableStart + row * RowStride + column)))
            .ToArray());

    public static IReadOnlyList<ushort> Addresses { get; } = Array.AsReadOnly(StateAddresses.Concat(TableAddresses).ToArray());

    private readonly Func<IReviewedChargeBackend> createBackend;
    private readonly Action<TimeSpan> pause;
    private readonly TimeSpan timeout;

    public GpuPowerEcReader(Func<IReviewedChargeBackend>? createBackend = null, Action<TimeSpan>? pause = null,
        TimeSpan? operationTimeout = null)
    {
        this.createBackend = createBackend ?? (() => new SystemManagementReviewedChargeBackend());
        this.pause = pause ?? Thread.Sleep;
        timeout = operationTimeout ?? ReviewedChargeProtocol.OperationTimeout;
        if (timeout <= TimeSpan.Zero || timeout > ReviewedChargeProtocol.OperationTimeout)
            throw new ArgumentOutOfRangeException(nameof(operationTimeout));
    }

    /// <summary>The only packet this class sends: the reviewed read of one address from the fixed list.</summary>
    public static ulong EncodeRead(ushort address) => Addresses.Contains(address)
        ? (0x0100UL << 32) | address
        : throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the GPU power read set.");

    public GpuPowerEcReadout Read(CancellationToken cancellationToken)
    {
        IReviewedChargeBackend backend;
        try { backend = createBackend(); }
        catch (Exception exception) { return GpuPowerEcReadout.Unavailable(Describe(exception)); }
        using (backend)
        {
            string instance;
            try
            {
                if (!Bounded(backend.IsAdministrator))
                    return GpuPowerEcReadout.Unavailable("需要以管理员身份运行");
                if (!ReviewedChargeProtocol.WmiSignatureMatches(Bounded(() => backend.ReadInterfaceSignature(timeout))))
                    return GpuPowerEcReadout.Unavailable("EC 接口与已核对的定义不一致，未读取");
                instance = ReviewedChargeProtocol.SelectReviewedInstance(Bounded(() => backend.ReadInterfaceInstances(timeout)));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return GpuPowerEcReadout.Unavailable(Describe(exception));
            }

            var bytes = new Dictionary<ushort, byte>();
            var unread = new List<ushort>();
            var retries = 0;
            string? failure = null;
            foreach (var address in Addresses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (failure is not null) { unread.Add(address); continue; }
                var packet = EncodeRead(address);
                byte? value = null;
                for (var attempt = 0; attempt < MaxAttempts && value is null; attempt++)
                {
                    if (attempt > 0) { retries++; pause(BusyPause); }
                    uint? raw;
                    try { raw = Bounded(() => backend.InvokeGetSetULong(instance, packet, timeout)); }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        // A deadline or a WMI error ends the whole read; the bytes already read are kept.
                        failure = Describe(exception);
                        break;
                    }
                    finally { pause(CallSpacing); }
                    // Only the low byte is the address asked for. A busy firmware can leave the
                    // upper bytes as FE, so only the full sentinel means the read did not happen.
                    if (raw is uint answer && answer != ReviewedChargeProtocol.FailureSentinel) value = (byte)answer;
                }
                if (value is { } read) bytes[address] = read;
                else unread.Add(address);
            }
            return new(bytes, unread, retries, failure);
        }
    }

    private T Bounded<T>(Func<T> operation)
    {
        var task = Task.Run(operation);
        try { return task.WaitAsync(timeout).GetAwaiter().GetResult(); }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new TimeoutException("EC 读取超时，已停止后续读取");
        }
    }

    // The report is shared by the user, so a failure is named by its kind, never by its message.
    private static string Describe(Exception exception) => exception switch
    {
        TimeoutException => "EC 读取超时，已停止后续读取",
        UnauthorizedAccessException => "需要以管理员身份运行",
        System.Management.ManagementException management => $"EC 接口不可用（WMI {management.ErrorCode}）",
        _ => $"EC 接口不可用（{exception.GetType().Name}）"
    };
}
