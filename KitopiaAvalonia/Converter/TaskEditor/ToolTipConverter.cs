using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Core.SDKs.CustomScenario;

namespace Kitopia.Converter.TaskEditor;

public class ToolTipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (CustomScenarioGloble.ToolTipConverters.ContainsKey(value.GetType()))
        {
            return CustomScenarioGloble.ToolTipConverters[value.GetType()].Invoke(value);
        }
        else return value.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}