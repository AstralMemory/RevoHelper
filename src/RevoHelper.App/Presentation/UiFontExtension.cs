using System.Windows.Markup;

namespace RevoHelper.App.Presentation;

/// <summary>Typography adjustment, not window scaling. The floating meter owns its own sizes.</summary>
[MarkupExtensionReturnType(typeof(double))]
public sealed class UiFontExtension(double size) : MarkupExtension
{
    public double Size { get; set; } = size;

    public static double Grow(double size) => size switch
    {
        <= 9 => size + 1.3,
        <= 11 => size + .8,
        _ => size + .5
    };

    public override object ProvideValue(IServiceProvider serviceProvider) => Grow(Size);
}
