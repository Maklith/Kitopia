using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Core.SDKs.Services.Config;
using PluginCore.Onnx;

namespace KitopiaAvalonia.Converter.OnnxModelManagerPage;

public class ModelInfoToRuntimeChangerCtr : IMultiValueConverter
{
   
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values[0] is not string||values[1] is not string )
        {
            return false;
        }

        return values[0].Equals(values[1]) ;
    }
}