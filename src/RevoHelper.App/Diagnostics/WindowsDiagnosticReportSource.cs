using System.Globalization;
using System.IO;
using System.Text.Json;
using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.GpuPower;

namespace RevoHelper.App.Diagnostics;

/// <summary>
/// The system side of a report. Everything here only reads: WMI names, one nvidia-smi query, the
/// mode readback, RevoHelper's own log files and, for the full report, the GPU power report.
/// A part that fails is named in the report with a fixed reason; the rest still comes through.
/// </summary>
public sealed class WindowsDiagnosticReportSource(
    Func<CancellationToken, ValueTask<PlatformModeSelection?>>? readMode,
    IGpuPowerDiagnosticSource? gpuPower,
    string runtimeLogDirectory,
    string chargeLogDirectory,
    Func<DateTimeOffset>? now = null) : IDiagnosticReportSource
{
    public const int ChargeLogLines = 30;
    private static readonly TimeSpan ModeTimeout = TimeSpan.FromSeconds(5);
    private static readonly HashSet<string> PlainNotes = new(StringComparer.Ordinal)
        { "hold", "hold-start", "release", "charge", "restored", "shutdown-hold", "leftover", "shutdown-below-limit" };
    private static readonly string[] NotePrefixes = ["retry", "restore-retry", "shutdown-hold failed", "status-write"];

    public async Task<DiagnosticSystemFacts> ReadAsync(DiagnosticReportDetail detail, CancellationToken cancellationToken)
    {
        var at = (now ?? (() => DateTimeOffset.Now))();
        var machine = Task.Run(ReadMachine, cancellationToken);
        var power = Task.Run(WindowsGpuDiagnosticSystemReader.ReadPowerOnline, cancellationToken);
        var nvidia = Task.Run(async () => await NvidiaPowerReading.ReadAsync(cancellationToken).ConfigureAwait(false), cancellationToken);
        var log = Task.Run(() => ReadRuntimeLog(runtimeLogDirectory, at), cancellationToken);
        var mode = ReadModeAsync(cancellationToken);
        var gpuReport = detail == DiagnosticReportDetail.Full ? ReadGpuPowerAsync(cancellationToken) : Task.FromResult<string?>(null);
        var chargeLog = detail == DiagnosticReportDetail.Full
            ? Task.Run(() => ReadChargeLog(chargeLogDirectory), cancellationToken)
            : Task.FromResult<IReadOnlyList<string>>([]);

        var machineResult = await Quietly(machine).ConfigureAwait(false);
        var powerResult = await Quietly(power).ConfigureAwait(false);
        var (gpus, gpuFailure) = await ReadGpusAsync(nvidia).ConfigureAwait(false);
        var logRead = await Quietly(log).ConfigureAwait(false);
        IReadOnlyList<string> lines = logRead.Item1 ?? [];
        var state = logRead.Item1 is null ? DiagnosticLogState.Unreadable : logRead.Item2;
        return new(
            machineResult,
            await mode.ConfigureAwait(false),
            powerResult,
            gpus,
            gpuFailure,
            lines,
            state,
            await Quietly(chargeLog).ConfigureAwait(false) ?? ["没能读取限充日志。"],
            await gpuReport.ConfigureAwait(false));
    }

    /// <summary>runtime.log lines written in the 24 hours before <paramref name="at"/>, oldest first.</summary>
    public static (IReadOnlyList<string> Lines, DiagnosticLogState State) ReadRuntimeLog(string directory, DateTimeOffset at)
    {
        var since = at - TimeSpan.FromHours(24);
        var lines = new List<string>();
        int found = 0, failed = 0;
        // The log moves to runtime.log.previous at 256 KB; the day may span both files.
        foreach (var name in new[] { "runtime.log.previous", "runtime.log" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            found++;
            if (ReadShared(path) is not { } content) { failed++; continue; }
            foreach (var line in content)
            {
                var space = line.IndexOf(' ');
                if (space > 0 && DateTimeOffset.TryParse(line[..space], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written)
                    && written >= since && written <= at + TimeSpan.FromMinutes(5))
                    lines.Add(line);
            }
        }
        var state = found == 0 ? DiagnosticLogState.NoFile
            : failed == found ? DiagnosticLogState.Unreadable
            : failed > 0 ? DiagnosticLogState.PartlyRead
            : DiagnosticLogState.Read;
        return (lines, state);
    }

    /// <summary>
    /// The last lines of the newest charge-limit log, cut down to numbers and fixed event names.
    /// The raw JSON is not passed on: a note can carry an exception message with a JSON-escaped path.
    /// </summary>
    public static IReadOnlyList<string> ReadChargeLog(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        var newest = new DirectoryInfo(directory).GetFiles("hold-log-*.jsonl")
            .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
        if (newest is null) return [];
        if (ReadShared(newest.FullName) is not { } content) return ["没能读取限充日志。"];
        var lines = content.Where(line => line.Length > 0).TakeLast(ChargeLogLines).Select(ProjectChargeLine).ToList();
        lines.Insert(0, newest.Name);
        return lines;
    }

    public static string ProjectChargeLine(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return "（无法识别的行）";
            string Number(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                    ? number.ToString(CultureInfo.InvariantCulture) : "?";
            string Flag(string name) =>
                root.TryGetProperty(name, out var value) ? value.ValueKind switch { JsonValueKind.True => "1", JsonValueKind.False => "0", _ => "?" } : "?";
            var at = root.TryGetProperty("At", out var time) && time.ValueKind == JsonValueKind.String &&
                     DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written)
                ? written.ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "?";
            var note = root.TryGetProperty("Note", out var text) && text.ValueKind == JsonValueKind.String ? NoteCategory(text.GetString()!) : "?";
            return $"{at} soc={Number("Soc")} hold={Flag("Holding")} thr={Number("Threshold")} timer={Number("TimerHigh")}/{Number("TimerLow")} " +
                   $"flags={Number("Flags")} ac={Flag("PowerOnline")} chg={Flag("Charging")} rate={Number("ChargeRateMw")} {note}";
        }
        catch (JsonException) { return "（无法识别的行）"; }
    }

    // "retry: Access to the path is denied." → retry(access-denied). The message itself stays out.
    private static string NoteCategory(string note)
    {
        if (PlainNotes.Contains(note)) return note;
        var prefix = NotePrefixes.FirstOrDefault(known => note.StartsWith(known + ":", StringComparison.Ordinal));
        if (prefix is null) return "other";
        var message = note[(prefix.Length + 1)..];
        var reason = message.Contains("full failure sentinel", StringComparison.OrdinalIgnoreCase) ? "ec-busy"
            : message.Contains("changed externally", StringComparison.OrdinalIgnoreCase) ? "ec-race"
            : message.Contains("denied", StringComparison.OrdinalIgnoreCase) || message.Contains("拒绝", StringComparison.Ordinal) ? "access-denied"
            : message.Contains("used by another process", StringComparison.OrdinalIgnoreCase) ? "file-in-use"
            : message.Contains("timed out", StringComparison.OrdinalIgnoreCase) || message.Contains("超时", StringComparison.Ordinal) ? "timeout"
            : "other";
        return $"{prefix}({reason})";
    }

    /// <summary>nvidia-smi's own output can say anything; the report gets one of a few fixed reasons.</summary>
    public static string? SafeGpuFailure(string? failure) => NvidiaPowerReading.SafeFailure(failure);

    // The app and the charge-limit process keep appending while the report reads. Null when the file could not be read.
    private static IReadOnlyList<string>? ReadShared(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line) lines.Add(line);
            return lines;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static DiagnosticMachine ReadMachine()
    {
        var machine = WindowsGpuDiagnosticSystemReader.ReadMachine();
        return new(machine.Manufacturer, machine.Model, machine.Family, machine.Baseboard, machine.Bios,
            machine.EmbeddedController, machine.Processor, machine.Windows);
    }

    private async Task<string> ReadModeAsync(CancellationToken cancellationToken)
    {
        if (readMode is null) return "未接入";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ModeTimeout);
        try { return GpuPowerDiagnosticCollector.DescribeMode(await readMode(timeout.Token).ConfigureAwait(false)); }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        { return "读取失败"; }
    }

    private async Task<string?> ReadGpuPowerAsync(CancellationToken cancellationToken)
    {
        if (gpuPower is null) return "未接入。";
        try { return (await gpuPower.CollectAsync(cancellationToken).ConfigureAwait(false)).Text; }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        { return $"生成失败（{exception.GetType().Name}）"; }
    }

    private static async Task<(IReadOnlyList<DiagnosticGpu> Gpus, string? Failure)> ReadGpusAsync(
        Task<(IReadOnlyList<NvidiaPowerReading> Readings, string? Failure)> task)
    {
        try
        {
            var (readings, failure) = await task.ConfigureAwait(false);
            return (readings.Select(gpu => new DiagnosticGpu(gpu.Name, gpu.Driver, Watts(gpu.EnforcedW), Watts(gpu.DefaultW), Watts(gpu.MaximumW))).ToArray(),
                SafeGpuFailure(failure));
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { return ([], $"nvidia-smi 读取失败（{exception.GetType().Name}）"); }
    }

    // "175.00" reads as 175; words such as N/A stay as the driver wrote them.
    private static string Watts(string raw) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var watts) && double.IsFinite(watts)
            ? watts.ToString("0.##", CultureInfo.InvariantCulture) : raw;

    private static async Task<T?> Quietly<T>(Task<T> task)
    {
        try { return await task.ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException) { return default; }
    }
}
