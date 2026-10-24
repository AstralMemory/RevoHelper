using System.Runtime.InteropServices;

// Diagnostic only. The private power-policy ABI is checked read-only before any use.
// ABI reference: NvAPIWrapper Native/GPU/Structures/PrivatePowerPolicies*V1.
internal static class NvidiaPowerPolicyProbe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Query(uint id);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Enumerate([Out, MarshalAs(UnmanagedType.LPArray, SizeConst = 64)] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Policy(IntPtr gpu, [In, Out] byte[] buffer);

    public static object Read()
    {
        var library = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory, "nvapi64.dll"));
        try
        {
            var query = Marshal.GetDelegateForFunctionPointer<Query>(NativeLibrary.GetExport(library, "nvapi_QueryInterface"));
            T Function<T>(uint id) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(query(id));
            var initialized = Function<Initialize>(0x0150E828)();
            if (initialized != 0) return new { Initialized = initialized };
            var handles = new IntPtr[64];
            var enumeration = Function<Enumerate>(0xE5AC921F)(handles, out var count);
            if (enumeration != 0 || count != 1) return new { Enumeration = enumeration, Count = count };
            static byte[] Buffer(int size)
            {
                var bytes = new byte[size];
                BitConverter.GetBytes(size | 0x10000).CopyTo(bytes, 0);
                return bytes;
            }
            var info = Buffer(184);
            var status = Buffer(72);
            return new
            {
                InfoResult = Function<Policy>(0x34206D86)(handles[0], info),
                StatusResult = Function<Policy>(0x70916171)(handles[0], status),
                Info = Enumerable.Range(0, info.Length / 4).Select(i => BitConverter.ToUInt32(info, i * 4)).ToArray(),
                Status = Enumerable.Range(0, status.Length / 4).Select(i => BitConverter.ToUInt32(status, i * 4)).ToArray()
            };
        }
        finally { NativeLibrary.Free(library); }
    }
}
