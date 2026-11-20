namespace RevoHelper.Hardware.Windows.Telemetry;

/// <summary>
/// Picks the readings for one device. The usual sensor names come first; when a CPU or GPU names
/// its sensors differently, a rule for the whole category takes over, so a new model or library
/// version does not show N/A just because a label changed. Values outside a physical range
/// (a 0 °C reading from an unsupported sensor, a negative clock) count as missing.
/// </summary>
public static class LibreSensorSelector
{
    public static SelectedTelemetry SelectCpu(IEnumerable<SensorDescriptor> sensors)
    {
        var all = sensors as IReadOnlyList<SensorDescriptor> ?? sensors.ToArray();
        var performanceClock = Maximum(all, SensorValueKind.Clock, name =>
            name.StartsWith("P-Core #", StringComparison.OrdinalIgnoreCase));
        var efficiencyClock = Maximum(all, SensorValueKind.Clock, name =>
            name.StartsWith("E-Core #", StringComparison.OrdinalIgnoreCase));
        var hasHybridClockLabels = performanceClock is not null || efficiencyClock is not null;
        return new(
            FindExact(all, SensorValueKind.Load, "CPU Total") ?? AverageCoreLoad(all),
            FindFirstExact(all, SensorValueKind.Temperature,
                "CPU Package", "Core Average", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "Package", "Core Max")
                // Any other die temperature under a new label ("CCD3 (Tdie)", "P-Core #2"), never a margin.
                ?? Maximum(all, SensorValueKind.Temperature, IsDieTemperatureName),
            FindFirstExact(all, SensorValueKind.Power, "CPU Package", "Package")
                ?? Maximum(all, SensorValueKind.Power, name => name.Contains("Package", StringComparison.OrdinalIgnoreCase)),
            // Average and "effective" clocks are not the operating clock; Windows' own clock covers a gap here.
            hasHybridClockLabels ? null : MaximumCoreClock(all),
            performanceClock,
            efficiencyClock,
            MaximumCoreUsagePercent: MaximumCoreLoad(all));
    }

    public static SelectedTelemetry SelectGpu(IEnumerable<SensorDescriptor> sensors)
    {
        var all = sensors as IReadOnlyList<SensorDescriptor> ?? sensors.ToArray();
        return new(
            FindFirstExact(all, SensorValueKind.Load, "GPU Core", "D3D 3D")
                // As Task Manager does: the busiest engine.
                ?? Maximum(all, SensorValueKind.Load, name => name.StartsWith("D3D ", StringComparison.OrdinalIgnoreCase)),
            FindExact(all, SensorValueKind.Temperature, "GPU Core")
                ?? Maximum(all, SensorValueKind.Temperature, name =>
                    !name.Contains("Memory", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)),
            FindFirstExact(all, SensorValueKind.Power, "GPU Power", "GPU Package", "GPU Board Power")
                ?? Maximum(all, SensorValueKind.Power, _ => true),
            FindExact(all, SensorValueKind.Clock, "GPU Core")
                ?? Maximum(all, SensorValueKind.Clock, name => name.Contains("Core", StringComparison.OrdinalIgnoreCase)),
            DedicatedMemoryMegabytes: FindExact(all, SensorValueKind.Memory, "GPU Memory Total"));
    }

    /// <summary>Physical limits for one reading; anything outside them is a sensor that is not really there.</summary>
    public static bool IsPlausible(SensorValueKind kind, double? value) =>
        value is { } number && double.IsFinite(number) && kind switch
        {
            SensorValueKind.Load => number is >= 0 and <= 100,
            SensorValueKind.Temperature => number is > 0 and < 150,
            SensorValueKind.Power => number is > 0 and < 2000,
            SensorValueKind.Clock => number is > 0 and < 20000,
            SensorValueKind.Memory => number > 0,
            _ => false
        };

    private static double? FindExact(
        IReadOnlyList<SensorDescriptor> sensors,
        SensorValueKind kind,
        string name)
    {
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (sensor.Kind == kind && IsPlausible(kind, sensor.Value) &&
                string.Equals(sensor.Name, name, StringComparison.OrdinalIgnoreCase))
                return sensor.Value;
        }

        return null;
    }

    private static double? FindFirstExact(
        IReadOnlyList<SensorDescriptor> sensors,
        SensorValueKind kind,
        params string[] names)
    {
        foreach (var name in names)
        {
            var value = FindExact(sensors, kind, name);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static double? Maximum(
        IReadOnlyList<SensorDescriptor> sensors,
        SensorValueKind kind,
        Func<string, bool> namePredicate)
    {
        double? maximum = null;
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (sensor.Kind == kind && IsPlausible(kind, sensor.Value) && namePredicate(sensor.Name) &&
                (maximum is null || sensor.Value > maximum))
                maximum = sensor.Value;
        }

        return maximum;
    }

    private static double? MaximumCoreClock(IReadOnlyList<SensorDescriptor> sensors) =>
        Maximum(sensors, SensorValueKind.Clock, IsNumberedCoreClock);

    private static bool IsNumberedCoreClock(string name)
    {
        // Intel uses "CPU Core #n"; AMD uses "Core #n". Keep effective clocks
        // and aggregate/uncore clocks separate from the existing operating-clock display.
        var prefixLength = name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase) ? 10
            : name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase) ? 6
            : 0;
        return prefixLength != 0 && name.Length > prefixLength &&
               name.AsSpan(prefixLength).IndexOfAnyExceptInRange('0', '9') < 0;
    }

    // A temperature of the processor die itself, under whatever label; not a "Distance to TjMax" margin.
    private static bool IsDieTemperatureName(string name) =>
        !name.Contains("Distance", StringComparison.OrdinalIgnoreCase) &&
        (name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("CCD", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Tdie", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));

    private static double? AverageCoreLoad(IReadOnlyList<SensorDescriptor> sensors)
    {
        double total = 0;
        var count = 0;
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (sensor.Kind != SensorValueKind.Load || !IsCoreLoadName(sensor.Name)) continue;
            if (!IsPlausible(SensorValueKind.Load, sensor.Value)) return null;
            total += sensor.Value!.Value;
            count++;
        }

        return count == 0 ? null : total / count;
    }

    private static double? MaximumCoreLoad(IReadOnlyList<SensorDescriptor> sensors)
    {
        double? maximum = null;
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (sensor.Kind != SensorValueKind.Load || !IsCoreLoadName(sensor.Name)) continue;

            // One invalid core still makes the busy-load safety reading unavailable.
            if (!IsPlausible(SensorValueKind.Load, sensor.Value)) return null;
            if (maximum is null || sensor.Value > maximum) maximum = sensor.Value;
        }

        return maximum;
    }

    private static bool IsCoreLoadName(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name,
            @"^(?:CPU Core|P-Core|E-Core) #\d+(?: Thread #\d+)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase |
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
