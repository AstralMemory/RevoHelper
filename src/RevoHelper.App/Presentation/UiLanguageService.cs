using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace RevoHelper.App.Presentation;

/// <summary>Display-only language state. No hardware values, command keys, file paths, or settings are transformed.</summary>
public sealed class UiLanguageService : INotifyPropertyChanged
{
    public static UiLanguageService Current { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Code { get; private set; } = "zh-CN";
    public void SetLanguage(string? code)
    {
        var normalized = LocalizedUiCopy.NormalizeLanguageCode(code);
        if (Code == normalized) return;
        Code = normalized;
        PropertyChanged?.Invoke(this, new(nameof(Code)));
    }
    public static string T(string text) => UiTextCatalog.Translate(text, Current.Code);
}

/// <summary>Keeps the original binding intact and translates only its read-only presentation value.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class UiTextExtension : MarkupExtension
{
    public UiTextExtension() { }
    public UiTextExtension(string text) => Text = text;
    public string Text { get; set; } = "";
    public Binding? Value { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Mode = BindingMode.OneWay, Converter = new UiTextConverter(Value?.StringFormat) };
        binding.Bindings.Add(Value ?? new Binding { Source = Text });
        binding.Bindings.Add(new Binding(nameof(UiLanguageService.Code)) { Source = UiLanguageService.Current });
        return binding.ProvideValue(serviceProvider);
    }
}

public sealed class UiTextConverter(string? stringFormat = null) : IMultiValueConverter
{
    public static UiTextConverter Instance { get; } = new();
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 0 || values[0] == DependencyProperty.UnsetValue) return "";
        var original = values[0]?.ToString() ?? "";
        if (!string.IsNullOrEmpty(stringFormat))
        {
            // WPF ignores a child's StringFormat inside MultiBinding, so preserve it explicitly.
            var format = stringFormat.StartsWith("{}", StringComparison.Ordinal) ? stringFormat[2..] : stringFormat;
            original = string.Format(culture, format, values[0]);
        }
        return UiTextCatalog.Translate(original, values.Length > 1 ? values[1] as string : "zh-CN");
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException("UI translations are display-only.");
}
