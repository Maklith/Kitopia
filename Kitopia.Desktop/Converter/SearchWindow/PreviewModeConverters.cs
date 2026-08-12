using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter.SearchWindow;

public sealed class PreviewModeToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isPreviewMode = value is true;
        return parameter switch
        {
            "List" => isPreviewMode ? new GridLength(360) : GridLength.Star,
            "Divider" => isPreviewMode ? new GridLength(1) : new GridLength(0),
            "Preview" => isPreviewMode ? GridLength.Star : new GridLength(0),
            _ => throw new ArgumentOutOfRangeException(nameof(parameter), parameter, null)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
