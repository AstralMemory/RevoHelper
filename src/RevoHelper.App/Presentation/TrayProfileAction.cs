using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

public sealed record TrayProfileAction(string Label, PlatformModeSelection Selection, string DotColor)
{
    public bool IsCurrent(PlatformModeSelection current) => Selection == current;

    public static IReadOnlyList<TrayProfileAction> All { get; } =
    [
        new("办公模式", new(PlatformMode.Office), "#22A967"),
        new("平衡模式", new(PlatformMode.Balanced), "#2185F4"),
        new("狂暴模式 · 静音", new(PlatformMode.TurboQuiet), "#9339DB"),
        new("狂暴模式 · 高能", new(PlatformMode.TurboHigh), "#9339DB"),
        new("自定义方案 1", new(PlatformMode.Custom, 1), "#D58A00"),
        new("自定义方案 2", new(PlatformMode.Custom, 2), "#D58A00"),
        new("自定义方案 3", new(PlatformMode.Custom, 3), "#D58A00")
    ];
}
