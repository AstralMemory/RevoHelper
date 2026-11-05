namespace RevoHelper.Hardware.Windows.Telemetry;

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
            FindExact(all, SensorValueKind.Load, "CPU Total"),
            FindFirstExact(all, SensorValueKind.Temperature,
                "CPU Package", "Core Average", "Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)"),
            Positive(FindFirstExact(all, SensorValueKind.Power, "CPU Package", "Package")),
            hasHybridClockLabels ? null : MaximumCoreClock(all),
            performanceClock,
            efficiencyClock,
            MaximumCoreUsagePercent: MaximumCoreLoad(all));
    }

    public static SelectedTelemetry SelectGpu(IEnumerable<SensorDescriptor> sensors)
    {
        var all = sensors as IReadOnlyList<SensorDescriptor> ?? sensors.ToArray();
        return new(
            FindFirstExact(all, SensorValueKind.Load, "GPU Core", "D3D 3D"),
            FindExact(all, SensorValueKind.Temperature, "GPU Core"),
            Positive(FindFirstExact(all, SensorValueKind.Power, "GPU Power", "GPU Package", "GPU Board Power")),
            FindExact(all, SensorValueKind.Clock, "GPU Core"),
            DedicatedMemoryMegabytes: Positive(FindExact(all, SensorValueKind.Memory, "GPU Memory Total")));
    }

    private static bool IsValid(double? value) =>
        value is { } number && double.IsFinite(number) && number >= 0;

    private static double? FindExact(
        IReadOnlyList<SensorDescriptor> sensors,
        SensorValueKind kind,
        string name)
    {
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (sensor.Kind == kind && IsValid(sensor.Value) &&
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
            if (sensor.Kind == kind && IsValid(sensor.Value) && namePredicate(sensor.Name) &&
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

    private static double? Positive(double? value) => value > 0 ? value : null;

    private static double? MaximumCoreLoad(IReadOnlyList<SensorDescriptor> sensors)
    {
        double? maximum = null;
        for (var index = 0; index < sensors.Count; index++)
        {
            var sensor = sensors[index];
            if (sensor.Kind != SensorValueKind.Load ||
                !System.Text.RegularExpressions.Regex.IsMatch(sensor.Name,
                @"^(?:CPU Core|P-Core|E-Core) #\d+(?: Thread #\d+)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.CultureInvariant)) continue;

            // One invalid core still makes the busy-load safety reading unavailable.
            if (!IsValid(sensor.Value) || sensor.Value > 100) return null;
            if (maximum is null || sensor.Value > maximum) maximum = sensor.Value;
        }

        return maximum;
    }
}
