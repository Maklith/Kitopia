using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PluginCore;
using Vanara.Windows.Shell;

namespace Kitopia.Desktop.Converter;

public class MouseQuickItemCanRemoveCtr : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is FileType fileType && fileType != FileType.None)
            return Visibility.Visible;
        else
            return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}