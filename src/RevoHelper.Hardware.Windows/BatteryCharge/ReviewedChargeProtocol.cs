namespace RevoHelper.Hardware.Windows.BatteryCharge;

/// <summary>
/// Independently encoded facts reviewed against this machine's local ACPI and EC
/// observations. This is not a generic Uniwill EC API or a compatibility claim.
/// </summary>
public static class ReviewedChargeProtocol
{
    public const uint FailureSentinel = 0xFEFEFEFE;
    public const string LogicalZeroInstance = @"ACPI\PNP0C14\1_0";
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan EcCallSpacing = TimeSpan.FromMilliseconds(10);

    public static IReadOnlyList<ushort> SnapshotAddresses { get; } = Array.AsReadOnly<ushort>(
        [0x0740, 0x0454, 0x0455, 0x047D, 0x0742, 0x0770, 0x07C3,
         0x07A6, 0x07B9, 0x07D0, 0x0490, 0x0497, 0x04AB, 0x9164]);

    public static bool ProfileMatches(ChargeMachineIdentity? identity) => identity is
    {
        Manufacturer: "MECHREVO", Model: "YAOSHI Series", BiosVersion: "N.1.18MRO24", EcMajor: 1, EcMinor: 25
    };

    public static bool SnapshotIdentityMatches(ChargeEcSnapshot snapshot) =>
        SnapshotIdentityMatches(snapshot.Identity, snapshot.Registers);

    public static bool SnapshotIdentityMatches(ChargeMachineIdentity? identity, IReadOnlyDictionary<ushort, uint> registers)
    {
        if (!ProfileMatches(identity)) return false;
        return Matches(0x0740, 0x1A) && Matches(0x0454, 0x59) && Matches(0x0455, 0) && Matches(0x047D, 0);

        bool Matches(ushort address, byte value) => registers.TryGetValue(address, out var raw)
            && raw != FailureSentinel && (byte)raw == value;
    }

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
        var names = instances.Select(instance => instance.InstanceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (instances.Count != 10 || names.Count != 10 || instances.Any(instance => !instance.Active)
            || Enumerable.Range(0, 10).Any(i => !names.Contains($@"ACPI\PNP0C14\1_{i}")))
            throw new InvalidDataException("WMI layout differs from the complete reviewed active 1_0..1_9 layout; no instance is probed.");
        return LogicalZeroInstance;
    }

    public static void ValidateTelemetry(ReviewedChargeTelemetry telemetry)
    {
        if (telemetry.WindowsPercent is < 0 or > 100 || telemetry.ChargeRateMw >= 300000
            || (telemetry.Charging && telemetry.Discharging))
            throw new InvalidDataException("Battery telemetry is invalid or ambiguous; no state is invented.");
    }
}
