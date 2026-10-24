using RevoHelper.Core.Control;

namespace RevoHelper.App.Presentation;

public sealed record LightingEffectOption(LightingEffect Value, string Label)
{
    public static LightingEffectOption From(LightingEffect effect) => new(effect, effect switch
    {
        LightingEffect.OemDefault => "机器最初始默认背光",
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
        LightingEffect.Impact => "冲突",
        LightingEffect.Meteor => "流星",
        LightingEffect.Colorful => "多彩",
        LightingEffect.Rotate => "旋转",
        _ => effect.ToString()
    });
}
