using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Core.SDKs.CustomScenario;

namespace Kitopia.Converter.TaskEditor;

public class ToolTipConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
        {
            return null;
        }
        if (value is CustomScenarioValue valueTuple)
        {
            if (CustomScenarioGloble.ToolTipConverters.ContainsKey(valueTuple.Type))
            {
                return CustomScenarioGloble.ToolTipConverters[valueTuple.Type].Invoke(valueTuple.Value);
            }
            else
                return valueTuple.Value?.ToString();
        }
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