namespace RevoHelper.Hardware.Windows.CpuPower;

public static class IntelRaplCounterCodec
{
    public static uint Delta32(ulong before, ulong after) =>
        unchecked((uint)after - (uint)before);

    public static double ToJoules(ulong powerUnitMsr, uint counterDelta)
    {
        var exponent = (int)((powerUnitMsr >> 8) & 0x1FUL);
        return counterDelta / (double)(1UL << exponent);
    }

    public static double ToSeconds(ulong powerUnitMsr, uint counterDelta)
    {
        var exponent = (int)((powerUnitMsr >> 16) & 0xFUL);
        return counterDelta / (double)(1UL << exponent);
    }
}
