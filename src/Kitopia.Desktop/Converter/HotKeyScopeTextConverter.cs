using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kitopia.Feature.Localization;
using PluginCore;
using Kitopia.Desktop.Features.Utils;

namespace Kitopia.Desktop.Converter;

public class HotKeyScopeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HotKeyModel model) return Lang.Get("lang.kitopia.all_processes");
        return HotKeyDisplay.ScopeDescription(model);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
