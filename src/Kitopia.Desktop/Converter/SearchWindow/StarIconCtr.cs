using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter.SearchWindow;

public class StarIconCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string str || value is not bool isStar) return null;

        if (isStar)
        {
            if (str == "FM")
                return Application.Current.TryGetResource("FluentFontFilled", null, out value) ? value : null;

            if (str == "F") return "\uF718";
        }
        else
        {
            if (str == "FM") return Application.Current.TryGetResource("FluentFont", null, out value) ? value : null;

            if (str == "F") return "\uF713";
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}