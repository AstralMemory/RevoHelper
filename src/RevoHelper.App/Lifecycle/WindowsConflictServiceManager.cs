using System.Diagnostics;
using System.IO;

namespace RevoHelper.App.Lifecycle;

public sealed class WindowsConflictServiceManager : IConflictServiceManager
{
    private static readonly string[] StopOrder = ["XtuService", "dptftcs", "ipfsvc"];
    private static readonly string[] RestoreOrder = ["ipfsvc", "dptftcs", "XtuService"];

    private readonly Func<string, string, CancellationToken, Task<int>> runServiceCommand;
    private readonly ConflictServiceStatusDetector detector;

    public WindowsConflictServiceManager()
        : this(RunServiceCommandAsync)
    {
    }

    public WindowsConflictServiceManager(
        Func<string, string, CancellationToken, Task<int>> runServiceCommand)
        : this(runServiceCommand, new ConflictServiceStatusDetector())
    {
    }

    public WindowsConflictServiceManager(
        Func<string, string, CancellationToken, Task<int>> runServiceCommand,
        ConflictServiceStatusDetector detector)
    {
        this.runServiceCommand = runServiceCommand;
        this.detector = detector;
    }

    public Task<IReadOnlyList<ConflictServiceStatus>> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(detector.Detect, cancellationToken);

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
            var exitCode = await runServiceCommand(command, serviceName, cancellationToken)
                .ConfigureAwait(false);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"服务 {serviceName} 执行 {command} 失败（sc.exe 退出代码 {exitCode}）。");
            }
        }
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
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        _ = await standardOutput.ConfigureAwait(false);
        _ = await standardError.ConfigureAwait(false);
        return process.ExitCode;
    }
}
