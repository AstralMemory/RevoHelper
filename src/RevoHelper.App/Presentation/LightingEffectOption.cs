using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

public sealed record LightingEffectOption(LightingEffect Value, string Label)
{
    public static LightingEffectOption From(LightingEffect effect) => new(effect, effect switch
    {
        LightingEffect.OemDefault => "默认背光",
        LightingEffect.Static => "单色静态",
        LightingEffect.Breathing => "呼吸",
        LightingEffect.Wave => "波浪",
        LightingEffect.Rainbow => "彩虹",
        LightingEffect.Reactive => "单点亮",
        LightingEffect.Ripple => "涟漪",
        LightingEffect.Raindrop => "雨滴",
        LightingEffect.ColorMarquee => "七彩跑马",
        LightingEffect.Fireworks => "烟火",
        LightingEffect.Aurora => "极光",
        LightingEffect.Game => "游戏",
        LightingEffect.Impact => "冲击",
        LightingEffect.Meteor => "流星",
        LightingEffect.Colorful => "多彩",
        LightingEffect.Rotate => "旋转",
        _ => effect.ToString()
    });
}

public sealed record LightingDirectionOption(LightingDirection Value, string Label)
{
    public static IReadOnlyList<LightingDirectionOption> All { get; } =
    [
        new(LightingDirection.None, "默认方向"),
        new(LightingDirection.Left, "从左到右"),
        new(LightingDirection.Right, "从右到左"),
        new(LightingDirection.Up, "从下到上"),
        new(LightingDirection.Down, "从上到下")
    ];
}
