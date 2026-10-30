using RevoHelper.Hardware.Windows.BatteryCharge;

namespace RevoHelper.Hardware.Windows.Tests.BatteryCharge;

public sealed class ReviewedChargeProtocolTests
{
    internal static ChargeMachineIdentity ReviewedIdentity => new("MECHREVO", "YAOSHI Series", "N.1.18MRO24", 1, 25);

    internal static Dictionary<ushort, uint> IdentityRegisters() => new()
    {
        [0x0740] = 0x011A, [0x0454] = 0x59, [0x0455] = 0, [0x047D] = 0
    };

    [Fact]
    public void ProfileIsExactAndDoesNotTreatMarketingFamilyAsCompatibility()
    {
        var identity = ReviewedIdentity;
        Assert.True(ReviewedChargeProtocol.ProfileMatches(identity));
        Assert.False(ReviewedChargeProtocol.ProfileMatches(identity with { Manufacturer = "mechrevo" }));
        Assert.False(ReviewedChargeProtocol.ProfileMatches(identity with { Model = "YAOSHI" }));
        Assert.False(ReviewedChargeProtocol.ProfileMatches(identity with { BiosVersion = "N.1.18MRO25" }));
        Assert.False(ReviewedChargeProtocol.ProfileMatches(identity with { EcMajor = 2 }));
        Assert.False(ReviewedChargeProtocol.ProfileMatches(identity with { EcMinor = 24 }));
        Assert.False(ReviewedChargeProtocol.ProfileMatches(null));
    }

    [Theory]
    [InlineData(0x0740)] [InlineData(0x0454)] [InlineData(0x0455)] [InlineData(0x047D)]
    [InlineData(0x0742)] [InlineData(0x0770)] [InlineData(0x07C3)] [InlineData(0x07A6)]
    [InlineData(0x07B9)] [InlineData(0x07D0)] [InlineData(0x0490)] [InlineData(0x0497)]
    [InlineData(0x04AB)] [InlineData(0x9164)]
    public void ReadEncoderHasOnlyReviewedAddressesAndReadOpcode(int address)
    {
        Assert.Equal((0x0100UL << 32) | (ushort)address, ReviewedChargeProtocol.EncodeRead((ushort)address));
    }

    [Theory]
    [InlineData(0)] [InlineData(0x0741)] [InlineData(0x087F)] [InlineData(0xFFFF)]
    public void ArbitraryReadIsRejected(int address) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewedChargeProtocol.EncodeRead((ushort)address));

    [Theory]
    [InlineData(0x07C3, 7, 4)] [InlineData(0x07C3, 4, 7)]
    [InlineData(0x07B9, 0, 60)] [InlineData(0x07B9, 60, 80)] [InlineData(0x07B9, 80, 0)]
    [InlineData(0x07B9, 0x80, 0xBC)] [InlineData(0x07B9, 0xD0, 0x80)]
    public void WriteEncoderSetsOnlyAddressAndDataLow(int address, int expected, int target)
    {
        var packet = ReviewedChargeProtocol.EncodeOwnedWrite((ushort)address, (byte)expected, (byte)target);
        Assert.Equal((ulong)(ushort)address | ((ulong)(byte)target << 16), packet);
        Assert.Equal(0UL, packet >> 24); // data_high, operation and reserved are all zero.
    }

    [Theory]
    [InlineData(0x0770, 0xFF, 4)] [InlineData(0x9164, 0, 80)] [InlineData(0x07C3, 7, 5)]
    [InlineData(0x07C3, 3, 4)] [InlineData(0x07B9, 0, 75)] [InlineData(0x07B9, 70, 80)]
    [InlineData(0x07B9, 0x80, 60)] [InlineData(0x07B9, 0, 0xD0)]
    public void OtherWritesOrChangingFirmwareOwnedBitSevenAreRejected(int address, int expected, int target) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ReviewedChargeProtocol.EncodeOwnedWrite((ushort)address, (byte)expected, (byte)target));

    [Fact]
    public void FullFailureSentinelIsCheckedBeforeMaskAndOtherHighBitsAreNotInterpreted()
    {
        Assert.Throws<InvalidDataException>(() => ReviewedChargeProtocol.DecodeByte(0xFEFEFEFE));
        Assert.Equal((byte)0xFE, ReviewedChargeProtocol.DecodeByte(0x000000FE));
        Assert.Equal((byte)0x1A, ReviewedChargeProtocol.DecodeByte(0xABCD811A));
    }

    [Fact]
    public void SnapshotByteRejectsMissingAndFailedRegisters()
    {
        var snapshot = new ChargeEcSnapshot(ReviewedIdentity,
            new Dictionary<ushort, uint> { [0x0740] = 0x811A, [0x07B9] = 0xFEFEFEFE },
            75, true, true, false, 30000, 90000, DateTimeOffset.UtcNow);
        Assert.Equal((byte)0x1A, snapshot.Byte(0x0740));
        Assert.Throws<KeyNotFoundException>(() => snapshot.Byte(0x9164));
        Assert.Throws<InvalidDataException>(() => snapshot.Byte(0x07B9));
    }

    [Fact]
    public void SnapshotIdentityRequiresAllFourVersionProjectBytes()
    {
        var registers = IdentityRegisters();
        Assert.True(ReviewedChargeProtocol.SnapshotIdentityMatches(ReviewedIdentity, registers));
        foreach (var address in registers.Keys.ToArray())
        {
            var missing = IdentityRegisters(); missing.Remove(address);
            Assert.False(ReviewedChargeProtocol.SnapshotIdentityMatches(ReviewedIdentity, missing));
            var wrong = IdentityRegisters(); wrong[address] ^= 1;
            Assert.False(ReviewedChargeProtocol.SnapshotIdentityMatches(ReviewedIdentity, wrong));
            var failed = IdentityRegisters(); failed[address] = 0xFEFEFEFE;
            Assert.False(ReviewedChargeProtocol.SnapshotIdentityMatches(ReviewedIdentity, failed));
        }
    }

    [Fact]
    public void CompleteActiveTenInstanceLayoutSelectsLogicalZeroRegardlessOfOrder()
    {
        var instances = Enumerable.Range(0, 10).Reverse()
            .Select(i => new ChargeWmiInstance($@"ACPI\PNP0C14\1_{i}", true)).ToArray();
        Assert.Equal(@"ACPI\PNP0C14\1_0", ReviewedChargeProtocol.SelectReviewedInstance(instances));
        Assert.Throws<InvalidDataException>(() => ReviewedChargeProtocol.SelectReviewedInstance(instances[..9]));
        Assert.Throws<InvalidDataException>(() => ReviewedChargeProtocol.SelectReviewedInstance(
            instances.Select((instance, i) => i == 0 ? instance with { Active = false } : instance).ToArray()));
        Assert.Throws<InvalidDataException>(() => ReviewedChargeProtocol.SelectReviewedInstance(
            instances.Select((instance, i) => i == 0 ? instances[1] : instance).ToArray()));
    }

    [Fact]
    public void MethodGuidIdNameAndBothTypesAreRequired()
    {
        var signature = new ChargeWmiSignature("{ABBC0F6F-8EA1-11d1-00A0-C90629100000}", "GetSetULong", 4,
            "Data", "UInt64", "Return", "UInt32");
        Assert.True(ReviewedChargeProtocol.WmiSignatureMatches(signature));
        Assert.False(ReviewedChargeProtocol.WmiSignatureMatches(signature with { ClassGuid = Guid.Empty.ToString() }));
        Assert.False(ReviewedChargeProtocol.WmiSignatureMatches(signature with { MethodId = 5 }));
        Assert.False(ReviewedChargeProtocol.WmiSignatureMatches(signature with { MethodName = "Other" }));
        Assert.False(ReviewedChargeProtocol.WmiSignatureMatches(signature with { InputCimType = "UInt32" }));
        Assert.False(ReviewedChargeProtocol.WmiSignatureMatches(signature with { OutputCimType = "UInt64" }));
        Assert.False(ReviewedChargeProtocol.WmiSignatureMatches(signature with { OutputName = "ReturnValue" }));
    }

    [Theory]
    [InlineData(-1, false, false, 0u)] [InlineData(101, false, false, 0u)]
    [InlineData(75, false, false, 300000u)] [InlineData(75, false, false, uint.MaxValue)]
    [InlineData(75, true, true, 30000u)]
    public void InvalidOrUnknownBatteryTelemetryIsRejected(int percent, bool charging, bool discharging, uint rate) =>
        Assert.Throws<InvalidDataException>(() => ReviewedChargeProtocol.ValidateTelemetry(
            new(percent, true, charging, discharging, rate, null)));

    [Fact]
    public void UnknownOptionalPercentIsNotInventedAndZeroRateIsValid()
    {
        ReviewedChargeProtocol.ValidateTelemetry(new(null, false, false, true, 0, null));
    }
}
