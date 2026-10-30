namespace RevoHelper.Hardware.Windows.BatteryCharge;

/// <summary>
/// Narrow control protocol and owned write transitions. Laptop model, BIOS names
/// and a developer machine's EC project/version are not eligibility criteria.
/// </summary>
public static class ReviewedChargeProtocol
{
    public const uint FailureSentinel = 0xFEFEFEFE;
    public const string LogicalZeroInstance = @"ACPI\PNP0C14\1_0";
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan EcCallSpacing = TimeSpan.FromMilliseconds(10);

    public static IReadOnlyList<ushort> SnapshotAddresses { get; } = Array.AsReadOnly<ushort>(
        [0x0742, 0x0770, 0x07C3,
         0x07A6, 0x07B9, 0x07D0, 0x0490, 0x0497, 0x04AB, 0x9164]);

    // Old leases were always created through this one fixed route. New leases
    // explicitly retain whichever single compatible route was actually selected.
    public static string SessionInstance(ChargeEcSnapshot snapshot) =>
        snapshot.InterfaceInstanceName ?? LogicalZeroInstance;

    public static byte DecodeByte(uint raw)
    {
        if (raw == FailureSentinel) throw new InvalidDataException("EC method returned the full failure sentinel.");
        // Extra high bits are retained by callers as opaque data, not interpreted as
        // adjacent bytes, written back, or used as a success/failure marker.
        return (byte)raw;
    }

    public static ulong EncodeRead(ushort address)
    {
        if (!SnapshotAddresses.Contains(address))
            throw new ArgumentOutOfRangeException(nameof(address), "Address is outside the reviewed read set.");
        return (0x0100UL << 32) | address;
    }

    public static ulong EncodeOwnedWrite(ushort address, byte expected, byte target)
    {
        var valid = address switch
        {
            0x07C3 => expected is 4 or 7 && target is 4 or 7,
            0x07B9 => IsThreshold(expected) && IsThreshold(target) && (expected & 0x80) == (target & 0x80),
            _ => false
        };
        if (!valid) throw new ArgumentOutOfRangeException(nameof(target), "Write is outside the reviewed owned-byte transitions.");
        // data_high, operation and reserved bytes deliberately remain zero.
        return address | ((ulong)target << 16);

        static bool IsThreshold(byte value) => (value & 0x7F) is 0 or 60 or 80;
    }

    public static bool WmiSignatureMatches(ChargeWmiSignature signature) =>
        Guid.TryParse(signature.ClassGuid, out var guid)
        && guid == new Guid("ABBC0F6F-8EA1-11d1-00A0-C90629100000")
        && signature is { MethodName: "GetSetULong", MethodId: 4, InputName: "Data", InputCimType: "UInt64",
            OutputName: "Return", OutputCimType: "UInt32" };

    public static string SelectReviewedInstance(IReadOnlyList<ChargeWmiInstance> instances)
    {
        var candidates = instances.Where(instance => instance.Active &&
            System.Text.RegularExpressions.Regex.IsMatch(instance.InstanceName ?? string.Empty,
                @"^ACPI\\PNP0C14\\[0-9]+_0$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)).ToArray();
        if (candidates.Length != 1 || instances.Count(instance => string.Equals(instance.InstanceName,
                candidates[0].InstanceName, StringComparison.OrdinalIgnoreCase)) != 1)
            throw new InvalidDataException("A single active compatible logical-zero WMI instance is required; no instances were probed.");
        return candidates[0].InstanceName;
    }

    public static void ValidateTelemetry(ReviewedChargeTelemetry telemetry)
    {
        if (telemetry.WindowsPercent is < 0 or > 100 || telemetry.ChargeRateMw >= 300000
            || (telemetry.Charging && telemetry.Discharging))
            throw new InvalidDataException("Battery telemetry is invalid or ambiguous; no state is invented.");
    }
}
