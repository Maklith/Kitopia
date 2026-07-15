using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.CustomScenario;

namespace Kitopia.Desktop.Converter;

public class HotKeySignNameToStringCtr : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (string)value!;

        if (s.Split("_")[0] == "Kitopia情景")
        {
            var uuid = s.Split("_")[1].Split("_")[0];
            var firstOrDefault = CustomScenarioManger.CustomScenarios.FirstOrDefault(e => e.Uuid == uuid);
            if (firstOrDefault is null) return s;

            s = s.Replace(uuid, firstOrDefault.Name);
            return s;
        }

        return s;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}