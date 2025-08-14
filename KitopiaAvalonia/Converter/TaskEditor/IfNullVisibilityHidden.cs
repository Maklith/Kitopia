#region

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

#endregion

namespace KitopiaAvalonia.Converter.TaskEditor;

public class IfNullVisibilityHidden : IValueConverter

{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return false;

        if (value is List<string> { Count: 0 }) return false;

        if (value is string s && string.IsNullOrEmpty(s)) return false;

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}