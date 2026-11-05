using System.Diagnostics;
using System.IO;

namespace RevoHelper.App.Lifecycle;

public sealed class WindowsConflictServiceManager : IConflictServiceManager
{
    private static readonly string[] StopOrder = ["XtuService", "dptftcs", "ipfsvc"];
    private static readonly string[] RestoreOrder = ["ipfsvc", "dptftcs", "XtuService"];
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StateCheckInterval = TimeSpan.FromMilliseconds(250);
    private const int MaximumStateChecks = 40;

    private readonly Func<string, string, CancellationToken, Task<int>> runServiceCommand;
    private readonly ConflictServiceStatusDetector detector;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Action<string, string>? diagnosticLog;

    public WindowsConflictServiceManager()
        : this(RunServiceCommandAsync, new ConflictServiceStatusDetector(), diagnosticLog: RuntimeDiagnosticLog.Write)
    {
    }

    public WindowsConflictServiceManager(
        Func<string, string, CancellationToken, Task<int>> runServiceCommand)
        : this(runServiceCommand, new ConflictServiceStatusDetector())
    {
    }

    public WindowsConflictServiceManager(
        Func<string, string, CancellationToken, Task<int>> runServiceCommand,
        ConflictServiceStatusDetector detector,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        Action<string, string>? diagnosticLog = null)
    {
        this.runServiceCommand = runServiceCommand;
        this.detector = detector;
        this.delay = delay ?? Task.Delay;
        this.diagnosticLog = diagnosticLog;
    }

    public Task<IReadOnlyList<ConflictServiceStatus>> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(detector.Detect, cancellationToken).WaitAsync(cancellationToken);

    public Task StopAsync(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken) =>
        ApplyAsync("stop", StopOrder, serviceNames, cancellationToken);

    public Task RestoreAsync(IReadOnlyList<string> serviceNames, CancellationToken cancellationToken) =>
        ApplyAsync("start", RestoreOrder, serviceNames, cancellationToken);

    private async Task ApplyAsync(
        string command,
        IReadOnlyList<string> order,
        IReadOnlyList<string> serviceNames,
        CancellationToken cancellationToken)
    {
        var requested = new HashSet<string>(serviceNames, StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(StopOrder, StringComparer.OrdinalIgnoreCase);
        var unknown = requested.FirstOrDefault(serviceName => !known.Contains(serviceName));
        if (unknown is not null)
        {
            throw new ArgumentException($"不允许管理未知服务：{unknown}", nameof(serviceNames));
        }

        foreach (var serviceName in order.Where(requested.Contains))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ApplyToServiceAsync(command, serviceName, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ApplyToServiceAsync(string command, string serviceName, CancellationToken cancellationToken)
    {
        var targetState = command == "stop" ? "Stopped" : "Running";
        var lastState = "Unobserved";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        try
        {
            var observed = await ObserveAsync().ConfigureAwait(false);
            Trace($"request service={serviceName} command={command} state={lastState}");
            if (MatchesTarget(observed))
            {
                Trace($"completed service={serviceName} command={command} state={lastState} already=true");
                return;
            }

            var exitCode = await runServiceCommand(command, serviceName, timeout.Token)
                .WaitAsync(timeout.Token).ConfigureAwait(false);
            Trace($"command service={serviceName} command={command} exit={exitCode}");
            for (var check = 0; check < MaximumStateChecks; check++)
            {
                observed = await ObserveAsync().ConfigureAwait(false);
                if (MatchesTarget(observed))
                {
                    Trace($"completed service={serviceName} command={command} state={lastState} exit={exitCode}");
                    return;
                }
                // A stale/racing SCM command may fail while the requested state
                // is already being reached. Only a fresh terminal observation
                // decides success; an unrelated failure is not retried.
                if (exitCode != 0 && !observed.IsTransitioning)
                    throw new InvalidOperationException(
                        $"服务 {serviceName} 执行 {command} 失败（sc.exe 退出代码 {exitCode}；状态 {lastState}）。");
                if (check < MaximumStateChecks - 1)
                    await delay(StateCheckInterval, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            }
            throw TimedOut();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Trace($"timeout service={serviceName} command={command} state={lastState}");
            throw TimedOut();
        }
        catch (Exception exception)
        {
            Trace($"failed service={serviceName} command={command} state={lastState} error={exception.GetType().Name}");
            throw;
        }

        async Task<ConflictServiceStatus> ObserveAsync()
        {
            timeout.Token.ThrowIfCancellationRequested();
            var statuses = await DetectAsync(timeout.Token).ConfigureAwait(false);
            var current = statuses.First(status => string.Equals(status.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase));
            if (!string.Equals(lastState, current.NativeState, StringComparison.OrdinalIgnoreCase))
                Trace($"state service={serviceName} command={command} value={current.NativeState}");
            lastState = current.NativeState;
            if (!current.IsInstalled || current.StatusText == "状态不可用")
                throw new InvalidOperationException(
                    $"服务 {serviceName} {current.StatusText}，未能确认 {command} 完成（状态 {lastState}）。");
            return current;
        }

        bool MatchesTarget(ConflictServiceStatus current) =>
            current.IsInstalled && string.Equals(current.NativeState, targetState, StringComparison.OrdinalIgnoreCase);

        TimeoutException TimedOut() => new(
            $"服务 {serviceName} 执行 {command} 未在10秒内到达 {targetState}（最后状态 {lastState}）。");
    }

    private void Trace(string message)
    {
        try { diagnosticLog?.Invoke("intel-service", message); }
        catch (Exception) { /* Diagnostics do not change service operation results. */ }
    }

    private static async Task<int> RunServiceCommandAsync(
        string command,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(serviceName);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows 服务控制程序 sc.exe。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(OperationTimeout);
        var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).WaitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryStopCommandProcess();
            throw new TimeoutException($"服务 {serviceName} 的 {command} 控制命令超过10秒未结束。");
        }
        catch
        {
            TryStopCommandProcess();
            throw;
        }

        void TryStopCommandProcess()
        {
            // End only this sc.exe helper, never the service or any descendants.
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception) { }
        }
    }
}
