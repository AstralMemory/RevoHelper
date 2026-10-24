using System.Diagnostics;
using System.Globalization;

namespace RevoHelper.Hardware.Windows.OemControl;

public static class NvidiaGpuPowerLimitReader
{
    public static async ValueTask<double?> ReadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
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
            start.ArgumentList.Add("--query-gpu=enforced.power.limit");
            start.ArgumentList.Add("--format=csv,noheader,nounits");
            process = Process.Start(start);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0) return null;
            var rows = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return rows.Length == 1 && double.TryParse(rows[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var watts)
                ? watts : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (process is { HasExited: false })
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
            return null;
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
}
