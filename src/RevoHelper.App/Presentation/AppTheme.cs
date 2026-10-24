using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;

namespace RevoHelper.App.Presentation;

/// <summary>
/// Colors for neutral UI surfaces only. Mode colors, hardware colors and user-selected
/// lighting colors are intentionally not rewritten when the theme changes.
/// </summary>
public static class AppTheme
{
    public const string Light = "light";
    public const string Dark = "dark";

    private sealed record ThemeColor(string LightColor, string DarkColor);

    private static readonly IReadOnlyDictionary<string, ThemeColor> Colors =
        new ReadOnlyDictionary<string, ThemeColor>(new Dictionary<string, ThemeColor>
        {
            ["SurfaceBrush"] = new("#FFFFFF", "#171C25"),
            ["RaisedBrush"] = new("#F5F7FB", "#202735"),
            ["LineBrush"] = new("#DCE3ED", "#3F4B60"),
            ["TextBrush"] = new("#17202D", "#EEF2F8"),
            ["MutedBrush"] = new("#718095", "#B1BED2"),
            ["QuietTextBrush"] = new("#8795A8", "#A7B6CD"),
            ["ControlSurfaceBrush"] = new("#F8FAFD", "#252E3D"),
            ["ControlHoverBrush"] = new("#E5EBF4", "#354257"),
            ["ControlSelectedBrush"] = new("#E9EEF5", "#344358"),
            ["ControlBorderBrush"] = new("#CBD7E6", "#4B5B74"),
            ["DisabledTrackBrush"] = new("#D6DEE9", "#536178"),
            ["ChartSurfaceBrush"] = new("#FFFFFF", "#1B2330"),
            ["ChartBorderBrush"] = new("#D5DDE8", "#43516A"),
            ["ChartGridBrush"] = new("#E2E8F0", "#344157"),
            ["ChartAxisBrush"] = new("#708095", "#B0BDD1"),
            // Pastel areas stay light in both themes, so their copy must stay dark.
            ["AccentSurfaceTextBrush"] = new("#17202D", "#17202D"),
            ["AccentSurfaceMutedBrush"] = new("#66758C", "#66758C"),
            ["ProfileDescriptionBrush"] = new("#66758C", "#66758C")
        });

    /// <summary>
    /// A mechanical migration map for neutral XAML color literals. Only brush-valued
    /// neutral properties should use these keys, not lighting colors or colored fills.
    /// Each alias preserves its exact original color in light mode.
    /// </summary>
    private static IReadOnlyDictionary<string, string> NeutralAliases { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["#FFFFFF"] = "SurfaceBrush",
            ["#F5F7FB"] = "RaisedBrush",
            ["#F4F7FB"] = "RaisedBrush",
            ["#F7F9FC"] = "ControlSurfaceBrush",
            ["#F8FAFD"] = "ControlSurfaceBrush",
            ["#F1F4F9"] = "ControlSurfaceBrush",
            ["#F1F5FA"] = "ControlSurfaceBrush",
            ["#F3F6FA"] = "ControlHoverBrush",
            ["#E5EBF4"] = "ControlHoverBrush",
            ["#E9EEF5"] = "ControlSelectedBrush",
            ["#EFF3F8"] = "ControlSurfaceBrush",
            ["#DCE3ED"] = "LineBrush",
            ["#D7E0EB"] = "LineBrush",
            ["#E3E9F1"] = "LineBrush",
            ["#D9E1EB"] = "LineBrush",
            ["#CBD7E6"] = "ControlBorderBrush",
            ["#CCD6E4"] = "ControlBorderBrush",
            ["#C8D2DF"] = "ControlBorderBrush",
            ["#CFD9E7"] = "ControlBorderBrush",
            ["#D2DDEA"] = "ControlBorderBrush",
            ["#D2DCE9"] = "ControlBorderBrush",
            ["#D3DCE8"] = "ControlBorderBrush",
            ["#D5DDE8"] = "ControlBorderBrush",
            ["#C7D1DE"] = "ControlBorderBrush",
            ["#C7D7E8"] = "ControlBorderBrush",
            ["#E1E8F1"] = "LineBrush",
            ["#E3E8F0"] = "LineBrush",
            ["#DFE5EE"] = "LineBrush",
            ["#D6DEE9"] = "DisabledTrackBrush",
            ["#17202D"] = "TextBrush",
            ["#182231"] = "TextBrush",
            ["#718095"] = "MutedBrush",
            ["#738095"] = "MutedBrush",
            ["#607086"] = "MutedBrush",
            ["#66758C"] = "MutedBrush",
            ["#52647C"] = "MutedBrush",
            ["#7B8798"] = "QuietTextBrush",
            ["#7D8DA5"] = "QuietTextBrush",
            ["#7D8EA6"] = "QuietTextBrush",
            ["#8190A4"] = "QuietTextBrush",
            ["#8793A5"] = "QuietTextBrush",
            ["#8795A8"] = "QuietTextBrush",
            ["#8EA0B8"] = "QuietTextBrush",
            ["#91A0B5"] = "QuietTextBrush",
            ["#93A0B2"] = "QuietTextBrush",
            ["#9AA7B8"] = "QuietTextBrush",
            ["#AAB4C2"] = "QuietTextBrush"
        });

    public static IReadOnlyDictionary<string, string> NeutralBrushKeys { get; } =
        new ReadOnlyDictionary<string, string>(NeutralAliases.ToDictionary(
            pair => pair.Key,
            pair => $"Neutral{pair.Key[1..]}Brush",
            StringComparer.OrdinalIgnoreCase));

    public static string Normalize(string? themeCode) =>
        string.Equals(themeCode, Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;

    public static void Apply(ResourceDictionary resources, string? themeCode)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var isDark = Normalize(themeCode) == Dark;
        foreach (var pair in Colors)
        {
            resources[pair.Key] = CreateBrush(isDark ? pair.Value.DarkColor : pair.Value.LightColor);
        }

        foreach (var pair in NeutralAliases)
        {
            var color = isDark ? Colors[pair.Value].DarkColor : pair.Key;
            resources[NeutralBrushKeys[pair.Key]] = CreateBrush(color);
        }

        // Native WPF inputs and their popups should inherit legible neutral colors too.
        resources[SystemColors.WindowBrushKey] = resources["SurfaceBrush"];
        resources[SystemColors.WindowTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.ControlBrushKey] = resources["ControlSurfaceBrush"];
        resources[SystemColors.ControlTextBrushKey] = resources["TextBrush"];
        resources[SystemColors.GrayTextBrushKey] = resources["MutedBrush"];
        resources[SystemColors.ScrollBarBrushKey] = resources["RaisedBrush"];
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
