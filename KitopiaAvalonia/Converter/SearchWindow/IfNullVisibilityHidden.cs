#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Vanara.Windows.Shell;

#endregion

namespace Kitopia.Converter.SearchWindow;

public class IfNullVisibilityHidden : IValueConverter

{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || (int)value == 0) return false;

        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}