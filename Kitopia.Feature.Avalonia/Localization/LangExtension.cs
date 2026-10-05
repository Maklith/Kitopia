using System.Globalization;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Kitopia.Feature.Localization;

namespace Kitopia.Feature.Avalonia.Localization;

public sealed class LangExtension : MarkupExtension, IMultiValueConverter
{
    private readonly string? _key;

    public LangExtension() { }
    public LangExtension(string key) => _key = key.StartsWith("{}", StringComparison.Ordinal) ? key[2..] : key;

    public BindingBase? Value { get; set; }
    public string? FallbackKey { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding
        {
            Converter = this,
            Bindings = { new Binding(nameof(Lang.Keys)) { Source = Lang.Current } }
        };
        if (Value is not null) binding.Bindings.Add(Value);
        return binding;
    }

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Value is null) return Lang.Get(_key ?? string.Empty);
        if (values.Count < 2 || values[1] == AvaloniaProperty.UnsetValue)
            return AvaloniaProperty.UnsetValue;
        if (_key is null && FallbackKey is not null && string.IsNullOrWhiteSpace(values[1]?.ToString()))
            return Lang.Get(FallbackKey);
        return _key is null ? values[1] is Enum value ? Lang.Get(value) : Lang.Get(values[1]?.ToString() ?? string.Empty)
            : Lang.Format(_key, values[1]);
    }
}
