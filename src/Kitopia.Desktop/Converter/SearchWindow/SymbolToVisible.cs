using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter.SearchWindow;

public class SymbolToVisible : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int d && d != 0) return true;

        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}