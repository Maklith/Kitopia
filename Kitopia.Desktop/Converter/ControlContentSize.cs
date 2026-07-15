using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter;

public class ControlContentSize : IMultiValueConverter
{
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var control = values[0] as double? ?? 0;
        var parent = values[1] as Thickness? ?? new Thickness(0);
        return control - parent.Left - parent.Right;
    }
}