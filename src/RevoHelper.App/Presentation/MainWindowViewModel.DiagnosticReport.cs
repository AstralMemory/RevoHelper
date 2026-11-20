using System.Globalization;
using System.IO;
using System.Text;
using RevoHelper.App.Diagnostics;
using RevoHelper.Core.Hardware;

namespace RevoHelper.App.Presentation;

// Settings → diagnostic report: a read-only text for the developer. Making a report never
// touches the clipboard; the user reads it first and then chooses to copy or save it.
public sealed partial class MainWindowViewModel
{
    // Cancelled when the app shuts down, so a full report (about 10 seconds) does not hold up the exit.
    private readonly CancellationTokenSource diagnosticReportLifetime = new();
    private bool isDiagnosticReportBusy;
    private DiagnosticReportDetail? diagnosticReportDetail;
    private DateTimeOffset diagnosticReportAt;
    private string diagnosticReportText = "";
    private string diagnosticReportStatusText = "";

    public bool CanGenerateDiagnosticReport => diagnosticReport is not null && !isDiagnosticReportBusy && !isShuttingDown;

    public bool IsDiagnosticReportBusy => isDiagnosticReportBusy;

    public string DiagnosticReportText
    {
        get => diagnosticReportText;
        private set
        {
            if (diagnosticReportText == value) return;
            diagnosticReportText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnosticReport));
            OnPropertyChanged(nameof(DiagnosticReportSummary));
        }
    }

    public bool HasDiagnosticReport => diagnosticReportText.Length > 0;

    public string DiagnosticReportSummary => diagnosticReportDetail is { } detail && HasDiagnosticReport
        ? $"{(detail == DiagnosticReportDetail.Brief ? "简短" : "完整")}报告 · {diagnosticReportText.Length.ToString(CultureInfo.InvariantCulture)} 字"
        : "";

    public string DiagnosticReportStatusText
    {
        get => diagnosticReportStatusText;
        private set
        {
            if (diagnosticReportStatusText == value) return;
            diagnosticReportStatusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDiagnosticReportStatus));
        }
    }

    public bool HasDiagnosticReportStatus => diagnosticReportStatusText.Length > 0;

    /// <summary>A file name for the save dialog; only the report kind and the time it was made.</summary>
    public string DiagnosticReportFileName =>
        $"RevoHelper-{(diagnosticReportDetail == DiagnosticReportDetail.Full ? "full" : "brief")}-report-" +
        $"{diagnosticReportAt.ToLocalTime().ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.txt";

    /// <returns>True when a new report is shown. Nothing is copied.</returns>
    public async Task<bool> GenerateDiagnosticReportAsync(DiagnosticReportDetail detail, CancellationToken cancellationToken)
    {
        if (!CanGenerateDiagnosticReport) return false;
        SetDiagnosticReportBusy(true);
        DiagnosticReportStatusText = detail == DiagnosticReportDetail.Brief
            ? "正在生成简短报告…"
            : "正在生成完整报告，读取显卡功耗大约需要 10 秒…";
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, diagnosticReportLifetime.Token);
        try
        {
            // What the window shows right now, taken before anything is awaited.
            var state = CaptureDiagnosticState();
            var facts = await diagnosticReport!.ReadAsync(detail, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var at = utcNow().ToLocalTime();
            var text = DiagnosticReportFormatter.Format(state, facts, detail, at, DiagnosticPrivacy.ForCurrentUser());
            diagnosticReportDetail = detail;
            diagnosticReportAt = at;
            DiagnosticReportText = text;
            OnPropertyChanged(nameof(DiagnosticReportSummary));
            DiagnosticReportStatusText = "已生成，还没有复制。请先看一遍内容，再选择复制或保存为文件。";
            TraceControl("diagnostic-report", $"{detail} {text.Length} chars");
            return true;
        }
        catch (OperationCanceledException)
        {
            // The last report, if any, stays as it was.
            DiagnosticReportStatusText = "已取消生成。";
            return false;
        }
        catch (Exception exception)
        {
            DiagnosticReportStatusText = $"生成失败：{exception.Message}";
            TraceControl("diagnostic-report", $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            SetDiagnosticReportBusy(false);
        }
    }

    private void CancelDiagnosticReport()
    {
        try { diagnosticReportLifetime.Cancel(); } catch (ObjectDisposedException) { }
        OnPropertyChanged(nameof(CanGenerateDiagnosticReport));
    }

    public void ReportDiagnosticReportCopied(bool copied) => DiagnosticReportStatusText = copied
        ? "已复制到剪贴板，可以粘贴到私信、评论或 Issue。"
        : "没能复制到剪贴板，请在框内全选后复制。";

    /// <summary>
    /// Writes <paramref name="text"/>, the report the user saw when they opened the save dialog. A
    /// newer report may have finished while the dialog was open; it is not what they chose to save.
    /// </summary>
    public void SaveDiagnosticReport(string path, string text)
    {
        if (text.Length == 0) return;
        try
        {
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            DiagnosticReportStatusText = $"已保存：{Path.GetFileName(path)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            DiagnosticReportStatusText = $"保存失败：{exception.Message}";
        }
    }

    private DiagnosticAppState CaptureDiagnosticState()
    {
        var capabilities = runtimeCapabilities;
        var features = capabilities is null
            ? []
            : Enum.GetValues<HardwareFeature>()
                .Where(feature => capabilities.Features.ContainsKey(feature))
                .Select(feature => new DiagnosticFeature(FeatureName(feature), Availability(capabilities.Features[feature].Availability),
                    FeatureReason(feature, capabilities.Features[feature])))
                .ToArray();
        var backends = capabilities?.CpuBackends
            .Select(backend => new DiagnosticFeature(BackendName(backend.Backend), Availability(backend.Availability), backend.Reason))
            .ToArray() ?? [];
        var charge = Experiments.Charge;
        var chargeLimit = charge.RecoveryPending ? "待恢复"
            : charge.IsBusy ? "处理中"
            : charge.IsEnabled ? $"开 {charge.SelectedLimitPercent.ToString(CultureInfo.InvariantCulture)}%"
            : "关";
        return new(
            typeof(MainWindowViewModel).Assembly
                .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion ?? ReleaseVersionText,
            SelectedProfileId,
            BatteryPercent,
            chargeLimit,
            charge.StatusText,
            IsOfficialConsoleRunning,
            OemConnectionStatusText,
            capabilities is not null,
            features,
            backends,
            Devices.ToArray(),
            ConflictServices.Select(service => $"{service.ServiceName}（{service.DisplayName}）：{service.StatusText}").ToArray(),
            Dashboard.Identity.ProcessorCoreCount,
            Dashboard.Identity.ProcessorThreadCount);
    }

    private string FeatureReason(HardwareFeature feature, HardwareFeatureCapability capability)
    {
        var shown = CapabilityReason(feature);
        return string.IsNullOrWhiteSpace(capability.Reason) || capability.Reason == shown ? shown : $"{shown}（{capability.Reason}）";
    }

    private static string FeatureName(HardwareFeature feature) => feature switch
    {
        HardwareFeature.OemPlatformModes => "整机模式",
        HardwareFeature.OemCpuPowerLimits => "CPU 功耗（官方）",
        HardwareFeature.OemFanCurves => "风扇曲线",
        HardwareFeature.GpuOutputModes => "显卡输出",
        HardwareFeature.Lighting => "灯光",
        HardwareFeature.LiquidCooling => "水冷",
        HardwareFeature.WindowsProcessorPolicy => "Windows 处理器策略",
        HardwareFeature.IntelRapl => "Intel RAPL",
        HardwareFeature.AmdSmu => "AMD SMU",
        _ => feature.ToString()
    };

    private static string BackendName(CpuControlBackend backend) => backend switch
    {
        CpuControlBackend.WindowsPowerPolicy => "Windows 电源策略",
        CpuControlBackend.IntelRapl => "Intel RAPL",
        CpuControlBackend.AmdSmu => "AMD SMU",
        _ => backend.ToString()
    };

    private static string Availability(CapabilityAvailability availability) => availability switch
    {
        CapabilityAvailability.Available => "可用",
        CapabilityAvailability.Unverified => "未确认",
        _ => "不可用"
    };

    private void SetDiagnosticReportBusy(bool busy)
    {
        if (isDiagnosticReportBusy == busy) return;
        isDiagnosticReportBusy = busy;
        OnPropertyChanged(nameof(IsDiagnosticReportBusy));
        OnPropertyChanged(nameof(CanGenerateDiagnosticReport));
    }
}
