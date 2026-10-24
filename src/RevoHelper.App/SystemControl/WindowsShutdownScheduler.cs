using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace RevoHelper.App.SystemControl;

public sealed record ShutdownCommandResult(int ExitCode, string ErrorText);

public sealed record ShutdownScheduleResult(
    bool Succeeded,
    bool IsScheduled,
    DateTimeOffset? DueAt,
    string Message);

public interface IShutdownCommandRunner
{
    Task<ShutdownCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

public interface IShutdownScheduler
{
    Task<ShutdownScheduleResult> ScheduleAsync(int minutes, CancellationToken cancellationToken);
    Task<ShutdownScheduleResult> CancelAsync(CancellationToken cancellationToken);
}

public sealed class WindowsShutdownScheduler(
    IShutdownCommandRunner? commandRunner = null,
    TimeProvider? timeProvider = null) : IShutdownScheduler
{
    public static IReadOnlyList<int> AllowedMinutes { get; } = Array.AsReadOnly([5, 15, 30, 60, 120]);
    private static readonly string ShutdownExecutable = Path.Combine(Environment.SystemDirectory, "shutdown.exe");

    private readonly IShutdownCommandRunner runner = commandRunner ?? new WindowsShutdownCommandRunner();
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<ShutdownScheduleResult> ScheduleAsync(
        int minutes,
        CancellationToken cancellationToken)
    {
        if (!AllowedMinutes.Contains(minutes))
        {
            return new(false, false, null, "定时关机仅支持 5、15、30、60 或 120 分钟。");
        }

        var seconds = checked(minutes * 60).ToString(CultureInfo.InvariantCulture);
        var result = await runner.RunAsync(ShutdownExecutable, ["/s", "/t", seconds], cancellationToken)
            .ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return Failure(result, "设置定时关机失败");
        }

        var dueAt = clock.GetUtcNow().AddMinutes(minutes);
        return new(true, true, dueAt, $"已使用 Windows 设置 {minutes} 分钟后关机。");
    }

    public async Task<ShutdownScheduleResult> CancelAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(ShutdownExecutable, ["/a"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0
            ? new(true, false, null, "Windows 定时关机已取消。")
            : Failure(result, "取消定时关机失败");
    }

    private static ShutdownScheduleResult Failure(ShutdownCommandResult result, string prefix)
    {
        var details = string.IsNullOrWhiteSpace(result.ErrorText)
            ? string.Empty
            : $"：{result.ErrorText.Trim()}";
        return new(false, false, null, $"{prefix}（退出码 {result.ExitCode}）{details}");
    }
}

public sealed class WindowsShutdownCommandRunner : IShutdownCommandRunner
{
    public async Task<ShutdownCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows 关机命令。");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new(process.ExitCode, await errorTask.ConfigureAwait(false));
    }
}
