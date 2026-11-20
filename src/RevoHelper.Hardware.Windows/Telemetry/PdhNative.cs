using System.Runtime.InteropServices;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>
/// Performance counters through PDH with their English names, so they work in every display
/// language. Shared by the CPU and GPU readers; every call tolerates a counter this Windows lacks.
/// </summary>
internal static class PdhNative
{
    private const uint FormatDouble = 0x00000200, NoCap100 = 0x00008000, MoreData = 0x800007D2;

    public static IntPtr OpenQuery() => PdhOpenQueryW(null, IntPtr.Zero, out var query) == 0 ? query : IntPtr.Zero;

    /// <summary>Zero when this Windows has no such counter.</summary>
    public static IntPtr Add(IntPtr query, string path) =>
        query != IntPtr.Zero && PdhAddEnglishCounterW(query, path, IntPtr.Zero, out var counter) == 0 ? counter : IntPtr.Zero;

    public static bool Collect(IntPtr query) => query != IntPtr.Zero && PdhCollectQueryData(query) == 0;

    public static void Close(IntPtr query)
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
    }

    public static double? Single(IntPtr counter)
    {
        if (counter == IntPtr.Zero) return null;
        return PdhGetFormattedCounterValue(counter, FormatDouble | NoCap100, out _, out var value) == 0 &&
               value.CStatus is 0 or 1 && double.IsFinite(value.DoubleValue) && value.DoubleValue >= 0
            ? value.DoubleValue : null;
    }

    /// <summary>Every instance of a wildcard counter; an instance without valid data yet is left out.</summary>
    public static List<(string Name, double Value)> Items(IntPtr counter)
    {
        var items = new List<(string, double)>();
        if (counter == IntPtr.Zero) return items;
        uint size = 0;
        if (PdhGetFormattedCounterArrayW(counter, FormatDouble | NoCap100, ref size, out _, IntPtr.Zero) != MoreData || size == 0)
            return items;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, FormatDouble | NoCap100, ref size, out var count, buffer) != 0) return items;
            var itemSize = Marshal.SizeOf<CounterItem>();
            for (var index = 0; index < count; index++)
            {
                var item = Marshal.PtrToStructure<CounterItem>(buffer + index * itemSize);
                if (item.Value.CStatus is not (0 or 1) || item.Name == IntPtr.Zero) continue;
                items.Add((Marshal.PtrToStringUni(item.Name) ?? "", item.Value.DoubleValue));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return items;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterValue { public uint CStatus; public double DoubleValue; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem { public IntPtr Name; public CounterValue Value; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize,
        out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);
}
