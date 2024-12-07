#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;

#endregion

namespace Kitopia.Converter;

public class ValueToEqualsParameterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == parameter;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }
}