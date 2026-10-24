using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemControl;
using RevoHelper.Hardware.Windows.OemStatus;

return await BootWaterProgram.RunAsync(args);

internal static class BootWaterProgram
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "RevoHelper", "BootWater");
    private static readonly string LogPath = Path.Combine(LogDirectory, "boot-water.log");

    public static async Task<int> RunAsync(string[] args)
    {
        Directory.CreateDirectory(LogDirectory);
        Log($"启动登录前水冷自动连接辅助程序（参数：{string.Join(' ', args)}）。");

        using var lifetime = new CancellationTokenSource();
        lifetime.CancelAfter(TimeSpan.FromSeconds(35));
        await using var statusSource = new GcuMqttStatusSource(frontendRole: GcuFrontendRole.BootWater);
        var statusTask = statusSource.RunAsync(
            _ => ValueTask.CompletedTask,
            exception =>
            {
                Log($"GCU 通道暂未就绪：{exception.Message}");
                return ValueTask.CompletedTask;
            },
            lifetime.Token);

        var controller = new GcuQuickControlsController(statusSource);
        var connector = new LiquidCoolingAutoConnector(
            controller.ReadLiquidCoolingAsync,
            controller.ConnectLiquidCoolingAsync,
            maxReadAttempts: 24,
            retryDelay: TimeSpan.FromMilliseconds(500));

        ControlResult result;
        try
        {
            result = await connector.TryConnectAsync(lifetime.Token);
            Log($"结果：{(result.Succeeded ? "成功" : "未连接")}；{result.Reason}");
        }
        catch (OperationCanceledException)
        {
            result = ControlResult.Rejected("35 秒启动窗口内未完成连接；登录后由 RevoHelper 继续尝试。");
            Log($"结果：未连接；{result.Reason}");
        }
        catch (Exception exception)
        {
            result = ControlResult.Rejected($"辅助程序异常：{exception.Message}");
            Log($"结果：未连接；{result.Reason}");
        }
        finally
        {
            await lifetime.CancelAsync();
            try
            {
                await statusTask;
            }
            catch (OperationCanceledException)
            {
                // The helper is deliberately short-lived.
            }
        }

        return result.Succeeded ? 0 : 2;
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // A startup helper must never stay resident because logging failed.
        }
        catch (UnauthorizedAccessException)
        {
            // SYSTEM normally owns this directory; manual non-elevated runs remain harmless.
        }
    }
}
