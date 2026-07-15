using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter;

public class WidthAndHeightToRectConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object parameter, CultureInfo culture)
    {
        var width = (double)values[0];
        var height = (double)values[1];
        return new Rect(0, 0, width, height);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter,
        CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}