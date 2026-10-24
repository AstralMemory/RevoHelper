using RevoHelper.Core.Control;
using RevoHelper.Hardware.Windows.OemStatus;

namespace RevoHelper.Hardware.Windows.Tests.OemStatus;

public sealed class LightingStatusReadbackTests
{
    [Theory]
    [InlineData(LightingZone.CoastLightbar)]
    [InlineData(LightingZone.Logo)]
    public async Task QuietLightbarZones_RequestStatusImmediatelyWithoutGraceDelay(LightingZone zone)
    {
        using var cancellation = new CancellationTokenSource();
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var pending = LightingStatusReadback.WaitAsync(zone, response.Task, () =>
        {
            requests++;
            response.SetResult("reply");
            return Task.CompletedTask;
        }, cancellation.Token);
        try
        {
            Assert.Equal(1, requests);
            Assert.Equal("reply", await pending);
        }
        finally
        {
            cancellation.Cancel();
            try { await pending; } catch (OperationCanceledException) { }
        }
    }

    [Theory]
    [InlineData(LightingZone.Keyboard)]
    [InlineData(LightingZone.LiquidCooler)]
    public async Task EventFirstZones_KeepTheirExistingUnsolicitedReplyPath(LightingZone zone)
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        var pending = LightingStatusReadback.WaitAsync(zone, response.Task,
            () => { requests++; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(0, requests);
        response.SetResult("event");
        Assert.Equal("event", await pending);
        Assert.Equal(0, requests);
    }

    [Theory]
    [InlineData(LightingZone.Keyboard)]
    [InlineData(LightingZone.CoastLightbar)]
    [InlineData(LightingZone.Logo)]
    [InlineData(LightingZone.LiquidCooler)]
    public async Task AlreadyReceivedReply_DoesNotRequestDuplicateStatus(LightingZone zone)
    {
        Assert.Equal("event", await LightingStatusReadback.WaitAsync(zone, Task.FromResult("event"),
            () => throw new InvalidOperationException("Unexpected duplicate query"), CancellationToken.None));
    }
}
