using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class NvidiaGpuPowerLimitReader
{
    public static async ValueTask<double?> ReadAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["--query-gpu=enforced.power.limit", "--format=csv,noheader,nounits"],
            TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        if (result.Output is not { } output) return null;
        var rows = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return rows.Length == 1 && double.TryParse(rows[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var watts)
            ? watts : null;
    }

    /// <summary>Runs nvidia-smi with fixed arguments. Output is null when it is missing, fails or runs out of time.</summary>
    internal static async ValueTask<NvidiaSmiResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo("nvidia-smi")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            process = Process.Start(start);
            if (process is null) return new(null, "nvidia-smi 未能启动");
            var errors = process.StandardError.ReadToEndAsync(deadline.Token);
            var output = await process.StandardOutput.ReadToEndAsync(deadline.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode == 0) return new(output, null);
            var detail = FirstLine(output) ?? FirstLine(await errors.ConfigureAwait(false));
            return new(null, $"nvidia-smi 退出码 {process.ExitCode}" + (detail is null ? "" : $"：{detail}"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new(null, exception switch
            {
                Win32Exception => "未找到 nvidia-smi（没有 NVIDIA 独显驱动）",
                OperationCanceledException => "nvidia-smi 超时",
                _ => $"nvidia-smi 失败：{exception.GetType().Name}"
            });
        }
        finally
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            }
            process?.Dispose();
        }
    }

    private static string? FirstLine(string? text)
    {
        var line = text?.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line is null ? null : line.Length > 120 ? line[..120] : line;
    }
}

internal sealed record NvidiaSmiResult(string? Output, string? Failure);
