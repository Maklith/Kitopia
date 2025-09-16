// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaAvalonia
// FileName:ClassesContainTheClass.cs
// Date: 2025/09/16 11:09
// FileEffect:

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace KitopiaAvalonia.Converter;

public class ClassesContainTheClass : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Control control)
        {
            var s = (string?)parameter;
            if (s == null) return false;
            return control.Classes.Contains(s);
        }

        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}