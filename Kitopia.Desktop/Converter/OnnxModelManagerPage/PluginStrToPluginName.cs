using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.Services.Plugin;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class PluginStrToPluginName : IValueConverter

{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string pluginSource) return null;

        if (string.Equals(pluginSource, "Kitopia", StringComparison.Ordinal))
        {
            return "Kitopia";
        }

        return PluginManager.GetPluginLocalInfoByPlgStr(pluginSource)?.PluginBaseInfo.Name;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}
