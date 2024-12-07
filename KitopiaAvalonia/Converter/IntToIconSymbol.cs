#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;

#endregion

namespace Kitopia.Converter;

public class IntToIconSymbol : IValueConverter

{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Convert.ToChar(value).ToString();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}