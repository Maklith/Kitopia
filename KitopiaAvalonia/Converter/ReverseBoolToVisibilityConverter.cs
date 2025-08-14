#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;

#endregion

namespace KitopiaAvalonia.Converter;

public class ReverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return false;

        if (value is bool && (bool)value) return false;

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}