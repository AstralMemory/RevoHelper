using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;

namespace RevoHelper.Hardware.Windows.GpuPower;

/// <summary>
/// Builds the GPU power report when the user asks for it. Every part only reads: WMI queries,
/// one nvidia-smi query, the OEM service's status request and the fixed EC read list. It never
/// switches modes or writes a value. A part that fails is named in the report; the rest stay.
/// </summary>
public sealed class GpuPowerDiagnosticCollector : IGpuPowerDiagnosticSource
{
    private static readonly TimeSpan OemTimeout = TimeSpan.FromSeconds(5);
    private readonly string appVersion;
    private readonly Func<CancellationToken, ValueTask<PlatformModeSelection?>>? readMode;
    private readonly Func<CancellationToken, ValueTask<string>>? readOemStatus;
    private readonly Func<GpuDiagnosticMachine> readMachine;
    private readonly Func<bool?> readPowerOnline;
    private readonly Func<CancellationToken, ValueTask<(IReadOnlyList<NvidiaPowerReading> Readings, string? Failure)>> readNvidia;
    private readonly Func<NvpcfDevice> readNvpcf;
    private readonly Func<CancellationToken, GpuPowerEcReadout> readEc;
    private readonly Func<DateTimeOffset> now;
    private readonly SemaphoreSlim gate = new(1, 1);

    public GpuPowerDiagnosticCollector(
        string appVersion,
        Func<CancellationToken, ValueTask<PlatformModeSelection?>>? readMode,
        Func<CancellationToken, ValueTask<string>>? readOemStatus,
        Func<GpuDiagnosticMachine>? readMachine = null,
        Func<bool?>? readPowerOnline = null,
        Func<CancellationToken, ValueTask<(IReadOnlyList<NvidiaPowerReading> Readings, string? Failure)>>? readNvidia = null,
        Func<NvpcfDevice>? readNvpcf = null,
        Func<CancellationToken, GpuPowerEcReadout>? readEc = null,
        Func<DateTimeOffset>? now = null)
    {
        this.appVersion = appVersion;
        this.readMode = readMode;
        this.readOemStatus = readOemStatus;
        this.readMachine = readMachine ?? WindowsGpuDiagnosticSystemReader.ReadMachine;
        this.readPowerOnline = readPowerOnline ?? WindowsGpuDiagnosticSystemReader.ReadPowerOnline;
        this.readNvidia = readNvidia ?? NvidiaPowerReading.ReadAsync;
        this.readNvpcf = readNvpcf ?? WindowsGpuDiagnosticSystemReader.ReadNvpcf;
        this.readEc = readEc ?? (token => new GpuPowerEcReader().Read(token));
        this.now = now ?? (() => DateTimeOffset.Now);
    }

    public async Task<GpuPowerDiagnosticReport> CollectAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The EC list takes several seconds (the firmware waits 50 ms per byte); the rest runs alongside.
            var ec = Task.Run(() => readEc(cancellationToken), cancellationToken);
            var machine = Task.Run(readMachine, cancellationToken);
            var power = Task.Run(readPowerOnline, cancellationToken);
            var nvpcf = Task.Run(readNvpcf, cancellationToken);
            var nvidia = Task.Run(async () => await readNvidia(cancellationToken).ConfigureAwait(false), cancellationToken);
            var mode = ReadModeAsync(cancellationToken);
            var oem = ReadOemAsync(cancellationToken);

            var machineResult = await Capture(machine, cancellationToken).ConfigureAwait(false);
            var powerResult = await Capture(power, cancellationToken).ConfigureAwait(false);
            var nvpcfResult = await Capture(nvpcf, cancellationToken).ConfigureAwait(false);
            var nvidiaResult = await Capture(nvidia, cancellationToken).ConfigureAwait(false);
            var oemResult = await Capture(oem, cancellationToken).ConfigureAwait(false);
            var ecResult = await Capture(ec, cancellationToken).ConfigureAwait(false);
            var snapshot = new GpuPowerDiagnosticSnapshot(
                now(),
                appVersion,
                await mode.ConfigureAwait(false),
                powerResult.Value,
                machineResult.Value,
                nvidiaResult.Value.Readings ?? [],
                nvidiaResult.Failure ?? NvidiaPowerReading.SafeFailure(nvidiaResult.Value.Failure),
                nvpcfResult.Value,
                nvpcfResult.Failure,
                oemResult.Value,
                oemResult.Failure,
                ecResult.Value ?? GpuPowerEcReadout.Unavailable(ecResult.Failure ?? "未读取"));
            return GpuPowerDiagnosticFormatter.Format(snapshot);
        }
        finally { gate.Release(); }
    }

    public static string DescribeMode(PlatformModeSelection? selection) => selection switch
    {
        null => "未读到",
        { Mode: PlatformMode.Office } => "办公",
        { Mode: PlatformMode.Balanced } => "平衡",
        { Mode: PlatformMode.TurboQuiet, IsBasicTurbo: true } => "狂暴",
        { Mode: PlatformMode.TurboQuiet } => "狂暴 · 静音",
        { Mode: PlatformMode.TurboHigh } => "狂暴 · 高能",
        { Mode: PlatformMode.Custom } => $"自定义 {selection.ProfileIndex}",
        _ => selection.Mode.ToString()
    };

    private async Task<string> ReadModeAsync(CancellationToken cancellationToken)
    {
        if (readMode is null) return "未接入";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OemTimeout);
        try { return DescribeMode(await readMode(timeout.Token).ConfigureAwait(false)); }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        { return "读取失败"; }
    }

    private async Task<IReadOnlyDictionary<string, string?>?> ReadOemAsync(CancellationToken cancellationToken)
    {
        if (readOemStatus is null) throw new InvalidOperationException("官方接口未接入");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OemTimeout);
        var payload = await readOemStatus(timeout.Token).ConfigureAwait(false);
        return GcuCustomPerformanceStatusParser.ReadReported(payload, GpuPowerDiagnosticFormatter.OemFields);
    }

    private static async Task<(T? Value, string? Failure)> Capture<T>(Task<T> task, CancellationToken cancellationToken)
    {
        try { return (await task.ConfigureAwait(false), null); }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return (default, exception switch
            {
                OperationCanceledException or TimeoutException => "读取超时",
                // The type only: a message can carry a file path into a report the user shares.
                _ => $"读取失败（{exception.GetType().Name}）"
            });
        }
    }
}
