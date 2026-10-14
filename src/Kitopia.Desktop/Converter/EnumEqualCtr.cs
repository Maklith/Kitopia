using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter;

public class EnumEqualCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || !value.GetType().IsAssignableTo(typeof(Enum)) || parameter is null) return false;

        if (((int)value) == Int32.Parse(parameter.ToString()!)) return true;

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}