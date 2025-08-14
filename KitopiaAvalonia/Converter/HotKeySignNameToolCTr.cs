using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KitopiaAvalonia.Converter;

public class HotKeySignNameToolCTr : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Join("_", values);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}