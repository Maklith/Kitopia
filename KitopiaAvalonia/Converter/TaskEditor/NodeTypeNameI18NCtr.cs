using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Core.SDKs.CustomScenario;
using Core.SDKs.Tools;

namespace Kitopia.Converter.TaskEditor;

public class NodeTypeNameI18NCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is CustomScenarioInputValue customScenarioInputValue)
        {
            return CustomScenarioGloble.GetI18N(customScenarioInputValue.Type.FullName);
        }

        return "";

    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}