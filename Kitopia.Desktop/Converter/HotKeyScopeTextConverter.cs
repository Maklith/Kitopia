using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PluginCore;

namespace Kitopia.Desktop.Converter;

public class HotKeyScopeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not HotKeyModel model) return "所有进程";
        return model.ScopeSummary;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
