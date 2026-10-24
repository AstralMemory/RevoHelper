using RevoHelper.App.Presentation;
using RevoHelper.Core.Control;

namespace RevoHelper.App.Tests.Presentation;

public sealed class CustomPerformanceEditBufferTests
{
    [Fact]
    public void DragValuesCollapseIntoOneFinalUnsentValue()
    {
        var buffer = new CustomPerformanceEditBuffer();
        for (var watts = 150; watts >= 70; watts -= 5) buffer.Set(CustomPerformanceSetting.GpuTgpWatts, watts);
        Assert.Equal(1, buffer.Count);
        Assert.True(buffer.TryTakeNext(out var edit));
        Assert.Equal(CustomPerformanceSetting.GpuTgpWatts, edit.Key);
        Assert.Equal(70, edit.Value);
        Assert.False(buffer.TryTakeNext(out _));
    }

    [Fact]
    public void TakingOneFieldDoesNotDiscardNewerUnsentFields()
    {
        var buffer = new CustomPerformanceEditBuffer();
        buffer.Set(CustomPerformanceSetting.GpuTgpWatts, 110);
        buffer.Set(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0);
        Assert.True(buffer.TryTakeNext(out var first));
        buffer.Set(CustomPerformanceSetting.GpuDynamicBoostEnabled, 1);
        buffer.Set(CustomPerformanceSetting.GpuTgpWatts, 100);
        Assert.Equal(CustomPerformanceSetting.GpuTgpWatts, first.Key);
        Assert.Equal(2, buffer.Count);
        Assert.True(buffer.TryTakeNext(out var second));
        Assert.Equal(new KeyValuePair<CustomPerformanceSetting, int>(CustomPerformanceSetting.GpuDynamicBoostEnabled, 1), second);
        Assert.True(buffer.TryTakeNext(out var third));
        Assert.Equal(new KeyValuePair<CustomPerformanceSetting, int>(CustomPerformanceSetting.GpuTgpWatts, 100), third);
    }

    [Fact]
    public void ProfileSwitchDropsAllUnsentValues()
    {
        var buffer = new CustomPerformanceEditBuffer();
        buffer.Set(CustomPerformanceSetting.GpuTgpWatts, 70);
        buffer.Set(CustomPerformanceSetting.GpuDynamicBoostEnabled, 0);
        buffer.Clear();
        Assert.Equal(0, buffer.Count);
        Assert.False(buffer.TryTakeNext(out _));
    }
}
