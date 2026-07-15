#region

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

#endregion

namespace Kitopia.Desktop.Converter;

public class DoubleHalfConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var doubleValue = (double)values[0] - (double)values[1];
        return doubleValue / 2;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}