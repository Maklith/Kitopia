using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Core.CustomScenario;
using PluginCore;

namespace KitopiaAvalonia.Converter.TaskEditor;

public class NodeTypeNameI18NCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CustomScenarioValue customScenarioInputValue)
            return CustomScenarioGloble.GetI18N(customScenarioInputValue.SerializeType.FullName);
        if (value is Type type) return CustomScenarioGloble.GetI18N(type.FullName);
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}