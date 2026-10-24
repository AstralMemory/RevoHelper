using System.Diagnostics;
using System.Globalization;

namespace RevoHelper.Hardware.Windows.CpuPower;

/// <summary>
/// Optional adapter for an externally supplied official RyzenAdj executable.
/// RevoHelper deliberately does not bundle or install legacy WinRing0 drivers.
/// </summary>
public sealed class RyzenAdjProcessClient : IRyzenAdjClient
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly string executablePath;

    public RyzenAdjProcessClient(string executablePath)
    {
        this.executablePath = Path.GetFullPath(executablePath);
    }

    public bool IsAvailable => File.Exists(executablePath);

    public string AvailabilityReason => IsAvailable
        ? "已找到外部 RyzenAdj；仍需通过 --info 真实回读确认兼容性。"
        : $"未找到兼容的 RyzenAdj：{executablePath}";

    public async ValueTask<RyzenAdjPowerLimits?> ReadPowerLimitsAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var result = await RunAsync(["--info"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && RyzenAdjInfoParser.TryParsePowerLimits(result.Output, out var limits)
            ? limits
            : null;
    }

    public async ValueTask<bool> ApplyPowerLimitsAsync(
        RyzenAdjPowerLimits limits,
        CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return false;
        }

        var result = await RunAsync(
            [
                $"--stapm-limit={ToMilliwatts(limits.StapmWatts)}",
                $"--slow-limit={ToMilliwatts(limits.SlowWatts)}",
                $"--fast-limit={ToMilliwatts(limits.FastWatts)}"
            ],
            cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public void Dispose()
    {
    }

    private static string ToMilliwatts(decimal watts) =>
        decimal.Round(watts * 1000, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    public static string GetDefaultExecutablePath(string applicationDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(applicationDirectory));
        if (directory.Name.Equals("probe", StringComparison.OrdinalIgnoreCase) &&
            directory.Parent?.Name.Equals("tools", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Path.Combine(directory.Parent.FullName, "ryzenadj", "ryzenadj.exe");
        }

        return Path.Combine(directory.FullName, "tools", "ryzenadj", "ryzenadj.exe");
    }

    private async Task<(int ExitCode, string Output)> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return (-1, string.Empty);
            }

            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CommandTimeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return (process.ExitCode, (await stdout.ConfigureAwait(false)) + Environment.NewLine + (await stderr.ConfigureAwait(false)));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return (-1, "RyzenAdj command timed out.");
        }
        catch
        {
            TryKill(process);
            return (-1, string.Empty);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
