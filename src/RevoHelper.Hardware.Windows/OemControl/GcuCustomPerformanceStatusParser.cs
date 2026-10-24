using System.Globalization;
using System.Text.Json;
using RevoHelper.Core.Control;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class GcuCustomPerformanceStatusParser
{
    public static CustomPerformanceState? Parse(string payload)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
        using (document)
        {
            var root = document.RootElement;
            var tccOffset = FindInt(root, "TccOffset") ?? FindInt(root, "CpuTccOffset");
            var tccSwitch = FindBool(root, "TccOffsetSwitch") ?? FindBool(root, "CpuTccOffsetSwitch");
            var tgp = FindInt(root, "ConfigurableTGPTarget") ?? FindInt(root, "GpuConfigurableTGPTarget");
            if (tccOffset is null && tgp is null) return null;
            var tjMax = FindInt(root, "TjMax") ?? 105;
            return new(
                FindBool(root, "OverClockingEnabled") ?? FindBool(root, "OverClockingSwitch") ?? false,
                tccSwitch ?? false,
                Math.Clamp(tjMax - (tccOffset ?? 10), 70, tjMax),
                FindInt(root, "PL4") ?? 0,
                FindBool(root, "PerformanceAndOverClockMenuSwitch") ??
                    FindBool(root, "CPUPerformanceAndOverClockMenuSwitch") ?? false,
                tgp ?? 0,
                FindBool(root, "DynamicBoostSwitch") ?? FindBool(root, "GpuDynamicBoostSwitch") ?? false,
                FindInt(root, "DynamicBoost") ?? FindInt(root, "GpuDynamicBoost") ?? 0,
                FindInt(root, "CoreClockOffsetOC") ?? FindInt(root, "GpuCoreClockOffsetOC") ?? 0,
                FindInt(root, "MemoryClockOffsetOC") ?? FindInt(root, "GpuMemoryClockOffsetOC") ?? 0);
        }
    }

    public static GpuPerformanceState? ParseGpu(string payload)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
        using (document)
        {
            var root = document.RootElement;
            var tgp = FindInt(root, "ConfigurableTGPTarget") ?? FindInt(root, "GpuConfigurableTGPTarget");
            if (tgp is null) return null;
            return new(
                FindBool(root, "OverClockingEnabled") ?? FindBool(root, "OverClockingSwitch") ?? false,
                tgp.Value,
                FindBool(root, "DynamicBoostSwitch") ?? FindBool(root, "GpuDynamicBoostSwitch") ?? false,
                FindInt(root, "DynamicBoost") ?? FindInt(root, "GpuDynamicBoost") ?? 0,
                FindInt(root, "CoreClockOffsetOC") ?? FindInt(root, "GpuCoreClockOffsetOC") ?? 0,
                FindInt(root, "MemoryClockOffsetOC") ?? FindInt(root, "GpuMemoryClockOffsetOC") ?? 0);
        }
    }

    public static bool? ParseGpuConfigurableTgpEnabled(string payload)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
        using (document)
        {
            return FindBool(document.RootElement, "ConfigurableTGPSwitch") ??
                   FindBool(document.RootElement, "GpuConfigurableTGPSwitch");
        }
    }

    public static GpuClockOffsetState? ParseGpuClockOffsets(string payload)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(payload); }
        catch (JsonException) { return null; }
        using (document)
        {
            var root = document.RootElement;
            var core = FindInt(root, "CoreClockOffsetOC");
            var memory = FindInt(root, "MemoryClockOffsetOC");
            var enabled = FindBool(root, "OverClockingSwitch") ?? FindBool(root, "OverClockingEnabled");
            var mode = FindInt(root, "OperatingMode");
            if (core is null || memory is null || enabled is null || mode is null) return null;

            GpuClockOffsetLimits? limits = null;
            var hwocCoreMin = FindInt(root, "CoreClockOffsetMinimumHWOC");
            var coreMin = hwocCoreMin ?? FindInt(root, "CoreClockOffsetMinimum");
            var coreMax = FindInt(root, "CoreClockOffsetMaximumHWOC");
            var memoryMin = FindInt(root, "MemoryClockOffsetMinimumHWOC");
            var memoryMax = FindInt(root, "MemoryClockOffsetMaximumHWOC");
            if (coreMin is { } cmin && coreMax is { } cmax && memoryMin is { } mmin && memoryMax is { } mmax)
            {
                var bounds = GpuClockOffsetLimits.InputBounds;
                // Some OEMs advertise the legacy core minimum as zero while accepting
                // signed HWOC offsets. A negative value is sent only after a user edit
                // and must still pass the single-setting OEM readback confirmation.
                var inputCoreMin = hwocCoreMin is null && cmin == 0 ? bounds.CoreMinimum : cmin;
                var limited = new GpuClockOffsetLimits(Math.Max(inputCoreMin, bounds.CoreMinimum),
                    Math.Min(cmax, bounds.CoreMaximum), Math.Max(mmin, bounds.MemoryMinimum),
                    Math.Min(mmax, bounds.MemoryMaximum));
                if (limited.CoreMinimum <= limited.CoreMaximum && limited.MemoryMinimum <= limited.MemoryMaximum)
                    limits = limited;
            }
            return new(mode == 3, enabled.Value, new(core.Value, memory.Value), limits, ParseGpuPowerLimits(root));
        }
    }

    internal static int? ParseOperatingMode(string payload) => ReadValue(payload, root => FindInt(root, "OperatingMode"));

    internal static int? ParseSetting(string payload, CustomPerformanceSetting setting) => ReadValue(payload, root => setting switch
    {
        CustomPerformanceSetting.CpuTemperatureWallEnabled => ReadBit(root, "TccOffsetSwitch", "CpuTccOffsetSwitch"),
        CustomPerformanceSetting.CpuTemperatureWallC =>
            (FindInt(root, "TjMax") ?? 105) - (FindInt(root, "TccOffset") ?? FindInt(root, "CpuTccOffset")),
        CustomPerformanceSetting.CpuBurstPowerLimitWatts => FindInt(root, "PL4"),
        CustomPerformanceSetting.CpuAdvancedMenuEnabled => ReadBit(root, "PerformanceAndOverClockMenuSwitch", "CPUPerformanceAndOverClockMenuSwitch"),
        CustomPerformanceSetting.GpuOverclockingEnabled => ReadBit(root, "OverClockingSwitch", "OverClockingEnabled"),
        CustomPerformanceSetting.GpuTgpWatts => FindInt(root, "ConfigurableTGPTarget") ?? FindInt(root, "GpuConfigurableTGPTarget"),
        CustomPerformanceSetting.GpuDynamicBoostEnabled => ReadBit(root, "DynamicBoostSwitch", "GpuDynamicBoostSwitch"),
        CustomPerformanceSetting.GpuDynamicBoostWatts => FindInt(root, "DynamicBoost") ?? FindInt(root, "GpuDynamicBoost"),
        CustomPerformanceSetting.GpuCoreOffsetMhz => FindInt(root, "CoreClockOffsetOC"),
        CustomPerformanceSetting.GpuMemoryOffsetMhz => FindInt(root, "MemoryClockOffsetOC"),
        _ => null
    });

    internal static GpuPowerLimits? ParseGpuPowerLimits(string payload)
    {
        try { using var document = JsonDocument.Parse(payload); return ParseGpuPowerLimits(document.RootElement); }
        catch (JsonException) { return null; }
    }

    private static GpuPowerLimits? ParseGpuPowerLimits(JsonElement root)
    {
        if (FindInt(root, "ConfigurableTGPMinimum") is not { } tmin ||
            FindInt(root, "ConfigurableTGPMaximum") is not { } tmax ||
            FindInt(root, "DynamicBoostMinimum") is not { } dmin ||
            FindInt(root, "DynamicBoostMaximum") is not { } dmax) return null;
        var limits = new GpuPowerLimits(Math.Max(25, tmin), Math.Min(250, tmax), Math.Max(0, dmin), Math.Min(50, dmax));
        return limits.TgpMinimum <= limits.TgpMaximum && limits.DynamicBoostMinimum <= limits.DynamicBoostMaximum ? limits : null;
    }

    private static int? ReadBit(JsonElement root, string name, string alternate) =>
        (FindBool(root, name) ?? FindBool(root, alternate)) is { } value ? (value ? 1 : 0) : null;

    private static int? ReadValue(string payload, Func<JsonElement, int?> read)
    {
        try { using var document = JsonDocument.Parse(payload); return read(document.RootElement); }
        catch (JsonException) { return null; }
    }

    private static int? FindInt(JsonElement element, string name)
    {
        var value = Find(element, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static bool? FindBool(JsonElement element, string name)
    {
        var value = Find(element, name);
        if (bool.TryParse(value, out var result)) return result;
        return int.TryParse(value, out var number) ? number != 0 : null;
    }

    private static string? Find(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                // Fan/Status uses flat CPU_/GPU_ fields; saved profiles use nested CPU/GPU objects.
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "CPU_" + name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(property.Name, "GPU_" + name, StringComparison.OrdinalIgnoreCase))
                    return property.Value.ToString();
                if (Find(property.Value, name) is { } nested) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) if (Find(item, name) is { } nested) return nested;
        }
        return null;
    }
}
