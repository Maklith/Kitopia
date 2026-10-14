using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class ModelInfoToRuntimeChangerCtr : IMultiValueConverter
{
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values[0] is not string || values[1] is not string) return false;

        return values[0].Equals(values[1]);
    }
}