using System;
using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace KitopiaAvalonia.Converter.ScreenCaptureWindow;

public class SizeToIntegerStringConverter : IValueConverter
{
    private static string format="{0:F0}x{1:F0}";
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Size size)
        {
            return string.Format(format, size.Width, size.Height);
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}