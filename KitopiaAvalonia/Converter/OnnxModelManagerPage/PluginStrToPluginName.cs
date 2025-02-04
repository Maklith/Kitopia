using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Core.SDKs.Services.Plugin;

namespace KitopiaAvalonia.Converter.OnnxModelManagerPage;

public class PluginStrToPluginName:IValueConverter

{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string plgStr)
        {
            return PluginManager.GetPluginByPlgStr(plgStr)?.Name;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }
}