using RevoHelper.App.SystemControl;

namespace RevoHelper.App.Tests.SystemControl;

public sealed class WindowsShutdownSchedulerTests
{
    [Fact]
    public void AllowedMinutes_UsesTheFiveProductPresets() =>
        Assert.Equal([5, 15, 30, 60, 120], WindowsShutdownScheduler.AllowedMinutes);

    [Fact]
    public async Task ScheduleAsync_UsesWindowsShutdownAndReturnsDueTime()
    {
        var runner = new FakeRunner();
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 2, 13, 0, 0, TimeSpan.FromHours(8)));
        var scheduler = new WindowsShutdownScheduler(runner, clock);

        var result = await scheduler.ScheduleAsync(15, default);

        Assert.True(result.Succeeded);
        Assert.True(result.IsScheduled);
        Assert.Equal(clock.GetUtcNow().AddMinutes(15), result.DueAt);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), runner.FileName);
        Assert.True(Path.IsPathFullyQualified(runner.FileName!));
        Assert.Equal(["/s", "/t", "900"], runner.Arguments);
    }

    [Fact]
    public async Task ScheduleAsync_RejectsUnknownDelayWithoutRunningACommand()
    {
        var runner = new FakeRunner();
        var result = await new WindowsShutdownScheduler(runner).ScheduleAsync(10, default);

        Assert.False(result.Succeeded);
        Assert.False(result.IsScheduled);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task CancelAsync_UsesWindowsAbortCommand()
    {
        var runner = new FakeRunner();
        var result = await new WindowsShutdownScheduler(runner).CancelAsync(default);

        Assert.True(result.Succeeded);
        Assert.False(result.IsScheduled);
        Assert.Equal(Path.Combine(Environment.SystemDirectory, "shutdown.exe"), runner.FileName);
        Assert.True(Path.IsPathFullyQualified(runner.FileName!));
        Assert.Equal(["/a"], runner.Arguments);
    }

    [Fact]
    public async Task NonzeroExit_IsReportedAsFailure()
    {
        var runner = new FakeRunner { Result = new(1190, "没有正在进行的关机。") };
        var result = await new WindowsShutdownScheduler(runner).CancelAsync(default);

        Assert.False(result.Succeeded);
        Assert.Contains("1190", result.Message, StringComparison.Ordinal);
        Assert.Contains("没有正在进行的关机", result.Message, StringComparison.Ordinal);
    }

    private sealed class FakeRunner : IShutdownCommandRunner
    {
        public ShutdownCommandResult Result { get; set; } = new(0, string.Empty);
        public int Calls { get; private set; }
        public string? FileName { get; private set; }
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ShutdownCommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            Calls++;
            FileName = fileName;
            Arguments = arguments;
            return Task.FromResult(Result);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
