namespace RevoHelper.Hardware.Windows.BatteryCharge;

public sealed record ChargeMachineIdentity(string Manufacturer, string Model, string BiosVersion, int EcMajor, int EcMinor);

public sealed record ChargeEcSnapshot(
    ChargeMachineIdentity Identity,
    IReadOnlyDictionary<ushort, uint> Registers,
    int? WindowsPercent,
    bool PowerOnline,
    bool Charging,
    bool Discharging,
    uint ChargeRateMw,
    uint? RemainingCapacity,
    DateTimeOffset ObservedAt)
{
    public byte Byte(ushort address) => Registers.TryGetValue(address, out var raw)
        ? ReviewedChargeProtocol.DecodeByte(raw)
        : throw new KeyNotFoundException($"Required EC observation 0x{address:X4} is missing.");
}

public interface IReviewedChargeTransport : IDisposable
{
    ChargeEcSnapshot ReadSnapshot();
    uint ReadByte(ushort address);
    void WriteOwnedByte(ushort address, byte expected, byte target);
}

// This seam allows every protocol and ownership test to run without WMI, elevation
// or hardware access. The production implementation is internal and lazy.
public interface IReviewedChargeBackend : IDisposable
{
    ChargeMachineIdentity ReadMachineIdentity(TimeSpan timeout);
    bool IsAdministrator();
    ChargeWmiSignature ReadInterfaceSignature(TimeSpan timeout);
    IReadOnlyList<ChargeWmiInstance> ReadInterfaceInstances(TimeSpan timeout);
    uint? InvokeGetSetULong(string instanceName, ulong packet, TimeSpan timeout);
    ReviewedChargeTelemetry ReadBatteryTelemetry(TimeSpan timeout);
}

public sealed record ChargeWmiSignature(string ClassGuid, string MethodName, int MethodId,
    string InputName, string InputCimType, string OutputName, string OutputCimType);

public sealed record ChargeWmiInstance(string InstanceName, bool Active);

public sealed record ReviewedChargeTelemetry(int? WindowsPercent, bool PowerOnline,
    bool Charging, bool Discharging, uint ChargeRateMw, uint? RemainingCapacity);
