using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.Messaging;
using Core.CustomScenario;
using PluginCore.CustomScenario;

namespace KitopiaAvalonia.Converter.TaskEditor;

public class AutoTriggersCtr : IValueConverter
{
    public CustomScenario CustomScenario;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CustomScenario customScenario)
        {
            CustomScenario = customScenario;
            var keyValuePair = parameter;
            var key = ((KeyValuePair<string, CustomScenarioTriggerInfo>)keyValuePair).Key;
            return customScenario.AutoTriggers.Contains(key);
        }

        return false;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            var keyValuePair = parameter;
            var key = ((KeyValuePair<string, CustomScenarioTriggerInfo>)keyValuePair).Key;
            if (b == CustomScenario.AutoTriggers.Contains(key)) return CustomScenario;

            WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg
                { Type = 1, Name = key, CustomScenario = CustomScenario });

            if (b)
            {
                CustomScenario.AutoTriggers.Add(key);
            }
            else
            {
                if (CustomScenario.AutoTriggers.Contains(key)) CustomScenario.AutoTriggers.Remove(key);
            }
        }

        return CustomScenario;
    }
}
