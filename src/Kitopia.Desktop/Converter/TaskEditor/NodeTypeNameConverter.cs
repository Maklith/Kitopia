using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.CustomScenario;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Converter.TaskEditor;

public class NodeTypeNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CustomScenarioValue customScenarioInputValue)
            return CustomScenarioGlobe.GetTypeNameKey(customScenarioInputValue.SerializeType.FullName);
        if (value is Type type) return CustomScenarioGlobe.GetTypeNameKey(type.FullName);
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
