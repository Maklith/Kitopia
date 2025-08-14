#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;

#endregion

namespace KitopiaAvalonia.Converter;

public class DoubleQuarterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var doubleValue = (double)value;
        return doubleValue / 4;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}