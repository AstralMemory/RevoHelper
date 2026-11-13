using RevoHelper.Core.Telemetry;

namespace RevoHelper.Core.Tests.Telemetry;

public sealed class DiscreteGpuPollGateTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ASleepingGpuIsNeverAsked()
    {
        var gate = new DiscreteGpuPollGate();
        for (var second = 0; second < 600; second++)
            Assert.False(gate.ShouldPoll(powered: false, Start.AddSeconds(second)));
        Assert.True(gate.Asleep);
        Assert.True(gate.ShouldPoll(powered: true, Start.AddSeconds(600)));
        Assert.False(gate.Asleep);
    }

    [Fact]
    public void AnIdleGpuIsLeftAloneLongEnoughToFallAsleep()
    {
        var gate = new DiscreteGpuPollGate();
        var second = 0;
        for (; second < DiscreteGpuPollGate.BaseIdleSamples; second++)
        {
            Assert.True(gate.ShouldPoll(true, Start.AddSeconds(second)));
            gate.Observe(0, Start.AddSeconds(second));
        }
        // Resting: no driver query for the whole rest, then the device reports it is off.
        for (var rest = 1; rest < DiscreteGpuPollGate.Rest.TotalSeconds; rest++, second++)
            Assert.False(gate.ShouldPoll(true, Start.AddSeconds(second)));
        Assert.False(gate.ShouldPoll(false, Start.AddSeconds(second)));
        Assert.True(gate.Asleep);
    }

    [Fact]
    public void ABusyGpuIsReadEverySecond()
    {
        var gate = new DiscreteGpuPollGate();
        for (var second = 0; second < 900; second++)
        {
            Assert.True(gate.ShouldPoll(true, Start.AddSeconds(second)));
            gate.Observe(second % 7 == 0 ? 1 : 40, Start.AddSeconds(second));
        }
    }

    [Fact]
    public void AGpuThatStaysPoweredIsRestedLessAndLessOften()
    {
        var gate = new DiscreteGpuPollGate();
        var skipped = 0;
        for (var second = 0; second < 1800; second++)
        {
            if (gate.ShouldPoll(true, Start.AddSeconds(second))) gate.Observe(0, Start.AddSeconds(second));
            else skipped++;
        }
        // Direct-connect mode: idle at 0% yet never sleeps. Most seconds must still be live.
        Assert.InRange(skipped, 1, 180);
    }

    [Fact]
    public void AnUnreadablePowerStateKeepsTheOldBehaviour()
    {
        var gate = new DiscreteGpuPollGate();
        for (var second = 0; second < 120; second++)
        {
            Assert.True(gate.ShouldPoll(null, Start.AddSeconds(second)));
            gate.Observe(0, Start.AddSeconds(second));
        }
    }
}
