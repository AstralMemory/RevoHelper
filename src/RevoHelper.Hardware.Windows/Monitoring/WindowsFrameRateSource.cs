using System.ComponentModel;
using System.Diagnostics;
using RevoHelper.Core.Monitoring;

namespace RevoHelper.Hardware.Windows.Monitoring;

/// <summary>
/// Read-only ETW render-present capture in an owned, hidden upstream PresentMon process. No game injection,
/// service installation, hardware queries, or CSV files. The main UI can be hidden without pausing capture.
/// </summary>
public sealed class WindowsFrameRateSource : IFrameRateSource
{
    private readonly object sync = new();
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly IProcessTargetProvider targets;
    private readonly TimeProvider timeProvider;
    private readonly string? executablePath;
    private readonly FrameTargetSelection selection = new(Environment.ProcessId);
    private readonly FrameRateAccumulator accumulator = new();
    private CancellationTokenSource? lifetimeCancellation;
    private Task? captureTask;
    private FrameRateStatus status = FrameRateStatus.Stopped;
    private string? errorDetail;
    private bool disposed;

    public WindowsFrameRateSource(
        IProcessTargetProvider? targets = null,
        TimeProvider? timeProvider = null,
        string? executablePath = null)
    {
        this.targets = targets ?? new WindowsProcessTargetProvider();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.executablePath = executablePath;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (captureTask is { IsCompleted: false }) return;
            lifetimeCancellation?.Dispose();
            lifetimeCancellation = new CancellationTokenSource();
            lock (sync)
            {
                status = FrameRateStatus.Starting;
                errorDetail = null;
                accumulator.Reset();
                RefreshTargetUnsafe();
            }
            // Extraction and process startup do not block the WPF dispatcher.
            captureTask = Task.Run(() => RunCaptureAsync(lifetimeCancellation.Token), CancellationToken.None);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public void SetPinnedTarget(ProcessTarget? target)
    {
        lock (sync)
        {
            selection.SetPinnedTarget(target);
            accumulator.SetTarget(selection.CurrentTarget);
        }
    }

    public FrameRateSnapshot GetSnapshot()
    {
        lock (sync)
        {
            var now = timeProvider.GetUtcNow();
            if (status is FrameRateStatus.Stopped or FrameRateStatus.Starting or FrameRateStatus.Unavailable)
            {
                return new(selection.CurrentTarget, null, now, status, errorDetail);
            }
            if (selection.PinnedTarget is not null && selection.CurrentTarget is null)
            {
                return new(selection.PinnedTarget, null, now, FrameRateStatus.TargetExited, "固定的程序已退出，请重新选择。");
            }
            return accumulator.GetSnapshot(now);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            lifecycle.Release();
        }
        // Retain semaphore object: a queued StartAsync can safely acquire it and observe disposed.
    }

    private async Task StopCoreAsync()
    {
        lifetimeCancellation?.Cancel();
        if (captureTask is not null)
        {
            // Once cleanup starts it must finish even if the caller cancels its wait.
            await captureTask.ConfigureAwait(false);
            captureTask = null;
        }
        lifetimeCancellation?.Dispose();
        lifetimeCancellation = null;
        lock (sync)
        {
            status = FrameRateStatus.Stopped;
            errorDetail = null;
            accumulator.Reset();
        }
    }

    public static ProcessStartInfo CreateStartInfo(string executablePath, string sessionName)
    {
        var info = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(executablePath),
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath))!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "--output_stdout", "--no_console_stats", "--v1_metrics", "--no_track_gpu", "--no_track_input",
            "--exclude", "dwm.exe", "--exclude", "RevoHelper.exe", "--session_name", sessionName
        })
        {
            info.ArgumentList.Add(argument);
        }
        return info;
    }

    private async Task RunCaptureAsync(CancellationToken cancellationToken)
    {
        var sessionName = $"RevoHelper.FPS.{Environment.ProcessId}.{Guid.NewGuid():N}";
        Process? process = null;
        Microsoft.Win32.SafeHandles.SafeFileHandle? job = null;
        Task? targetLoop = null;
        Task? errorReader = null;
        FileStream? executableLock = null;
        using var captureCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            var path = executablePath ?? await PresentMonExecutable.EnsureExtractedAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            executableLock = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            process = new Process { StartInfo = CreateStartInfo(path, sessionName) };
            if (!process.Start()) throw new InvalidOperationException("FPS 采集组件未能启动。");
            job = PresentMonProcessLifetime.AttachKillOnCloseJob(process);
            var parser = new PresentMonCsvParser();
            lock (sync) status = FrameRateStatus.WaitingForFrames;
            targetLoop = RefreshTargetsAsync(captureCancellation.Token);
            errorReader = ReadErrorAsync(process.StandardError, captureCancellation.Token);
            while (!captureCancellation.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(captureCancellation.Token).ConfigureAwait(false);
                if (line is null) break;
                if (!parser.TryRead(line, out var frame)) continue;
                lock (sync) accumulator.Add(frame, timeProvider.GetUtcNow());
            }
            if (!cancellationToken.IsCancellationRequested)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                lock (sync)
                {
                    status = FrameRateStatus.Unavailable;
                    errorDetail ??= $"FPS 采集已停止（{process.ExitCode}）；可关闭后重新开启。";
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal stop; stale data is not reused on the next start.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            lock (sync)
            {
                status = FrameRateStatus.Unavailable;
                errorDetail = "FPS 采集暂不可用：" + exception.Message;
            }
        }
        finally
        {
            captureCancellation.Cancel();
            if (process is not null)
            {
                try
                {
                    // Stop our unique ETW session first, allowing upstream to exit normally.
                    PresentMonProcessLifetime.StopOwnedTraceSession(sessionName);
                    if (!process.HasExited)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { process.Kill(); }
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
                {
                    // The owned job closes below and kills any remaining owned child.
                }
            }
            job?.Dispose();
            process?.Dispose();
            executableLock?.Dispose();
            if (targetLoop is not null) await IgnoreCancellationAsync(targetLoop).ConfigureAwait(false);
            if (errorReader is not null) await IgnoreCancellationAsync(errorReader).ConfigureAwait(false);
        }
    }

    private async Task RefreshTargetsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500), timeProvider);
        do
        {
            lock (sync) RefreshTargetUnsafe();
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private void RefreshTargetUnsafe() => accumulator.SetTarget(selection.Refresh(targets));

    private async Task ReadErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        // Drain stderr to avoid deadlocking a pipe; retain only a bounded useful failure message, no history/log file.
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("denied", StringComparison.OrdinalIgnoreCase))
            {
                lock (sync) errorDetail = line.Length <= 300 ? line : line[..300];
            }
        }
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}
