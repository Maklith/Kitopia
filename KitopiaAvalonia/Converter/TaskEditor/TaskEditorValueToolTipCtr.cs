// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaAvalonia
// FileName:TaskEditorValueToolTipCtr.cs
// Date: 2025/09/17 21:09
// FileEffect:


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Core.CustomScenario;
using PluginCore;
using PluginCore.CustomScenario;
using ValueType = KitopiaAvalonia.Windows.TaskEditors.ValueType;

namespace KitopiaAvalonia.Converter.TaskEditor;

public class TaskEditorValueToolTipCtr : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0) return parameter;
        ValueType? first = values.FirstOrDefault(e => e != null && e.GetType() == typeof(ValueType)) as ValueType?;
        KeyValuePair<string, CustomScenarioValue>? second =
            values.FirstOrDefault(e => e != null && e.GetType() != typeof(KeyValuePair<string, CustomScenarioValue>)) as
                KeyValuePair<string, CustomScenarioValue>?;
        CustomScenario? third =
            values.FirstOrDefault(e => e != null && e.GetType() == typeof(CustomScenario)) as CustomScenario;
        if (first == null || second == null || third == null) return parameter;
        var toolTipConverter = new ToolTipConverter();
        switch (first)
        {
            case ValueType.None:
                break;
            case ValueType.InputValue:
                third.InputValue.TryGetValue(second.Value.Key, out var value);
                if (value != null)
                    return toolTipConverter.Convert(value, targetType, null, culture);

                break;
            case ValueType.TempValue:
                third.TempValue.TryGetValue(second.Value.Key, out var value1);
                if (value1 != null)
                    return toolTipConverter.Convert(value1, targetType, null, culture);
                break;
            case ValueType.StoredValue:
                third.Values.TryGetValue(second.Value.Key, out var value2);
                if (value2 != null)
                    return toolTipConverter.Convert(value2, targetType, null, culture);
                break;
            case null:
                break;
            default:
                break;
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}