using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RevoHelper.Hardware.Windows.PowerPolicy;

public sealed partial class PowrProfPowerProfileApi : IPowerProfileApi
{
    public Guid GetActiveScheme()
    {
        var result = PowerGetActiveScheme(IntPtr.Zero, out var pointer);
        ThrowOnError(result, nameof(PowerGetActiveScheme));
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    public uint ReadAcValue(Guid scheme, Guid subgroup, Guid setting)
    {
        var schemeValue = scheme;
        var subgroupValue = subgroup;
        var settingValue = setting;
        var result = PowerReadACValueIndex(
            IntPtr.Zero, ref schemeValue, ref subgroupValue, ref settingValue, out var value);
        ThrowOnError(result, nameof(PowerReadACValueIndex));
        return value;
    }

    public uint ReadDcValue(Guid scheme, Guid subgroup, Guid setting)
    {
        var schemeValue = scheme;
        var subgroupValue = subgroup;
        var settingValue = setting;
        var result = PowerReadDCValueIndex(
            IntPtr.Zero, ref schemeValue, ref subgroupValue, ref settingValue, out var value);
        ThrowOnError(result, nameof(PowerReadDCValueIndex));
        return value;
    }

    public void WriteAcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        var schemeValue = scheme;
        var subgroupValue = subgroup;
        var settingValue = setting;
        ThrowOnError(PowerWriteACValueIndex(
            IntPtr.Zero, ref schemeValue, ref subgroupValue, ref settingValue, value),
            nameof(PowerWriteACValueIndex));
    }

    public void WriteDcValue(Guid scheme, Guid subgroup, Guid setting, uint value)
    {
        var schemeValue = scheme;
        var subgroupValue = subgroup;
        var settingValue = setting;
        ThrowOnError(PowerWriteDCValueIndex(
            IntPtr.Zero, ref schemeValue, ref subgroupValue, ref settingValue, value),
            nameof(PowerWriteDCValueIndex));
    }

    public void SetActiveScheme(Guid scheme)
    {
        var schemeValue = scheme;
        ThrowOnError(PowerSetActiveScheme(IntPtr.Zero, ref schemeValue), nameof(PowerSetActiveScheme));
    }

    private static void ThrowOnError(uint result, string operation)
    {
        if (result != 0)
        {
            throw new Win32Exception((int)result, $"{operation} 失败");
        }
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadACValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerReadDCValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, out uint valueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteACValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint valueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerWriteDCValueIndex(
        IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subgroupGuid, ref Guid settingGuid, uint valueIndex);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
