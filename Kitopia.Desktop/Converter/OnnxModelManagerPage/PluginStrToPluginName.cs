using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.Services.Plugin;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class PluginStrToPluginName : IValueConverter

{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string plgStr) return PluginManager.GetPluginLocalInfoByPlgStr(plgStr)?.PluginBaseInfo.Name;

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}