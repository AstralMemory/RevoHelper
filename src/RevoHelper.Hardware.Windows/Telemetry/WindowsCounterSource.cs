using System.Runtime.InteropServices;

namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>What Windows itself reports, with no driver installed. Null where this PC has no such counter.</summary>
/// <param name="CpuFrequencyMhz">The live average clock as Task Manager shows it (base clock × "% Processor Performance").</param>
/// <param name="CpuPackagePowerWatts">The processor's own energy meter (RAPL package domain), read through Windows.</param>
/// <param name="GraphicsPowerWatts">The integrated graphics plane of the same meter, where the processor has one.</param>
/// <param name="ZoneTemperatureC">The hottest ACPI thermal zone in a plausible range; the firmware's CPU temperature on laptops.</param>
public sealed record WindowsCounterReading(
    double? CpuUsagePercent,
    double? CpuFrequencyMhz,
    double? CpuPackagePowerWatts,
    double? GraphicsPowerWatts,
    double? ZoneTemperatureC)
{
    public static WindowsCounterReading Empty { get; } = new(null, null, null, null, null);
}

public interface IWindowsCounterSource : IDisposable
{
    WindowsCounterReading Read();
}

/// <summary>
/// Performance counters through PDH with their English names, so they work in every display
/// language and need neither WMI nor a kernel driver. A counter this Windows lacks is skipped;
/// rates need two reads, so they appear from the second call on. When PDH itself cannot be
/// used, CPU usage still comes from the kernel's own idle and busy times.
/// </summary>
public sealed class PdhWindowsCounterSource : IWindowsCounterSource
{
    // A thermal zone outside this range is a placeholder value (many boards report a fixed 27.8 °C).
    public const double MinimumZoneCelsius = 30, MaximumZoneCelsius = 110;
    private readonly object gate = new();
    private readonly SystemTimesUsage systemTimes = new();
    private IntPtr query;
    private IntPtr utility, performance, baseFrequency, actualFrequency, energy, zones;
    private bool opened, disposed;

    public WindowsCounterReading Read()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!opened) Open();
            var fallbackUsage = systemTimes.Read();
            if (!PdhNative.Collect(query))
                return WindowsCounterReading.Empty with { CpuUsagePercent = fallbackUsage };

            var usage = PdhNative.Single(utility);
            var basis = PdhNative.Single(baseFrequency);
            var percent = PdhNative.Single(performance);
            var actual = PdhNative.Single(actualFrequency);
            var frequency = basis is > 0 && percent is > 0 ? Math.Round(basis.Value * percent.Value / 100)
                : actual is > 0 ? Math.Round(actual.Value) : (double?)null;
            var meters = PdhNative.Items(energy);
            return new(
                usage is { } value ? Math.Clamp(value, 0, 100) : fallbackUsage,
                frequency,
                // The meter reports milliwatts.
                SumMilliwatts(meters, "_pkg"),
                SumMilliwatts(meters, "_pp1"),
                HottestZone(PdhNative.Items(zones).Select(item => item.Value)));
        }
    }

    /// <summary>ACPI reports tenths of a kelvin.</summary>
    public static double? HottestZone(IEnumerable<double> tenthsOfKelvin)
    {
        double? hottest = null;
        foreach (var raw in tenthsOfKelvin)
        {
            var celsius = Math.Round(raw / 10 - 273.15, 1);
            if (celsius is >= MinimumZoneCelsius and <= MaximumZoneCelsius && (hottest is null || celsius > hottest))
                hottest = celsius;
        }
        return hottest;
    }

    /// <summary>RAPL domains are named like rapl_package0_pkg; several packages add up.</summary>
    public static double? SumMilliwatts(IEnumerable<(string Name, double Value)> meters, string domainSuffix)
    {
        double? total = null;
        foreach (var (name, value) in meters)
        {
            if (!name.EndsWith(domainSuffix, StringComparison.OrdinalIgnoreCase) || !double.IsFinite(value) || value < 0) continue;
            total = (total ?? 0) + value / 1000;
        }
        return total is > 0 ? Math.Round(total.Value, 1) : null;
    }

    private void Open()
    {
        opened = true;
        try
        {
            query = PdhNative.OpenQuery();
            if (query == IntPtr.Zero) return;
            utility = PdhNative.Add(query, @"\Processor Information(_Total)\% Processor Utility");
            performance = PdhNative.Add(query, @"\Processor Information(_Total)\% Processor Performance");
            baseFrequency = PdhNative.Add(query, @"\Processor Information(_Total)\Processor Frequency");
            actualFrequency = PdhNative.Add(query, @"\Processor Information(_Total)\Actual Frequency");
            energy = PdhNative.Add(query, @"\Energy Meter(*)\Power");
            zones = PdhNative.Add(query, @"\Thermal Zone Information(*)\High Precision Temperature");
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            query = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            PdhNative.Close(query);
            query = IntPtr.Zero;
        }
    }
}

/// <summary>Total CPU usage from the kernel's idle and busy times; available on every Windows.</summary>
public sealed class SystemTimesUsage
{
    private ulong lastIdle, lastTotal;

    public double? Read()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
        var idleTicks = idle.Ticks;
        var totalTicks = kernel.Ticks + user.Ticks; // kernel time includes idle time
        var result = Usage(lastIdle, lastTotal, idleTicks, totalTicks);
        lastIdle = idleTicks;
        lastTotal = totalTicks;
        return result;
    }

    public static double? Usage(ulong lastIdle, ulong lastTotal, ulong idle, ulong total)
    {
        if (lastTotal == 0 || total <= lastTotal || idle < lastIdle) return null;
        var busy = 1 - (double)(idle - lastIdle) / (total - lastTotal);
        return Math.Round(Math.Clamp(busy * 100, 0, 100), 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low, High;
        public readonly ulong Ticks => (ulong)High << 32 | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
}
