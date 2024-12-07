using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace KitopiaAvalonia.Converter;

public class HotKey_KeyTypeToVisible : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var i = int.Parse(((int)value).ToString(), NumberStyles.BinaryNumber);

        var s = parameter.ToString();
        switch (s)
        {
            case "Ctrl":
                return (i & (1 << 3)) != 0;
            case "Shift":
                return (i & (1 << 2)) != 0;
            case "Alt":
                return (i & (1 << 1)) != 0;
            case "Win":
                return (i & 1) != 0;
            case "None":
                return i == 0;
            case "KeyName":
                return i != 0;
            default:
                return false;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}