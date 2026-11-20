using System.Globalization;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.GpuPower;

/// <summary>One GPU's power fields as nvidia-smi reports them; "N/A" where the driver has no value.</summary>
public sealed record NvidiaPowerReading(
    string Name, string Driver, string Vbios, string PState, string TemperatureC,
    string DrawW, string RequestedW, string EnforcedW, string DefaultW, string MinimumW, string MaximumW,
    string ClockMhz, string MaximumClockMhz)
{
    // Every field has been in nvidia-smi for many driver generations; one unknown field fails the whole query.
    public static readonly string[] Fields =
    [
        "name", "driver_version", "vbios_version", "pstate", "temperature.gpu",
        "power.draw", "power.limit", "enforced.power.limit", "power.default_limit", "power.min_limit", "power.max_limit",
        "clocks.gr", "clocks.max.gr"
    ];

    public static IReadOnlyList<NvidiaPowerReading> ParseCsv(string output)
    {
        var readings = new List<NvidiaPowerReading>();
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var values = line.Split(',').Select(Clean).ToArray();
            if (values.Length != Fields.Length) continue;
            readings.Add(new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11], values[12]));
        }
        return readings;
    }

    public static double? Watts(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var watts) && double.IsFinite(watts) && watts > 0
            ? watts : null;

    public static async ValueTask<(IReadOnlyList<NvidiaPowerReading> Readings, string? Failure)> ReadAsync(
        CancellationToken cancellationToken)
    {
        var result = await NvidiaGpuPowerLimitReader.RunAsync(
            ["--query-gpu=" + string.Join(',', Fields), "--format=csv,noheader,nounits"],
            TimeSpan.FromSeconds(8), cancellationToken).ConfigureAwait(false);
        if (result.Output is not { } output) return ([], SafeFailure(result.Failure) ?? "nvidia-smi 没有输出");
        var readings = ParseCsv(output);
        return readings.Count > 0 ? (readings, null) : ([], "nvidia-smi 输出无法识别");
    }

    /// <summary>
    /// The reports users send on get one of a few fixed reasons: nvidia-smi's own output after a
    /// failure can say anything, file paths included.
    /// </summary>
    public static string? SafeFailure(string? failure) => failure switch
    {
        null => null,
        _ when failure.StartsWith("未找到 nvidia-smi", StringComparison.Ordinal) => "未找到 nvidia-smi（没有 NVIDIA 独显驱动）",
        _ when failure.StartsWith("nvidia-smi 超时", StringComparison.Ordinal) => "nvidia-smi 超时",
        _ when failure.StartsWith("nvidia-smi 未能启动", StringComparison.Ordinal) => "nvidia-smi 未能启动",
        _ when failure.StartsWith("nvidia-smi 没有输出", StringComparison.Ordinal) => "nvidia-smi 没有输出",
        _ when failure.StartsWith("nvidia-smi 输出无法识别", StringComparison.Ordinal) => "nvidia-smi 输出无法识别",
        _ when failure.StartsWith("nvidia-smi 退出码 ", StringComparison.Ordinal) =>
            "nvidia-smi 退出码 " + new string(failure["nvidia-smi 退出码 ".Length..].TakeWhile(c => c == '-' || char.IsAsciiDigit(c)).ToArray()),
        _ => "nvidia-smi 读取失败"
    };

    // nvidia-smi writes "[N/A]" or "[Not Supported]" for fields a GPU does not report; the words are kept.
    private static string Clean(string value)
    {
        var text = value.Trim();
        if (text.StartsWith('[') && text.EndsWith(']')) text = text[1..^1].Trim();
        return text.Length == 0 ? "N/A" : text;
    }
}
