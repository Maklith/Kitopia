#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Vanara.Windows.Shell;

#endregion

namespace KitopiaAvalonia.Converter;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return Visibility.Collapsed;

        if (value is bool && (bool)value) return Visibility.Visible;

        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}