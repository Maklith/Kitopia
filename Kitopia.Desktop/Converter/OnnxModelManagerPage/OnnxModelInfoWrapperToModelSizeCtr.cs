using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class OnnxModelInfoWrapperToModelSizeCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OnnxModelInfoWrapper wrapper
            && OnnxModelSize.TryGetTotalBytes(wrapper.Model, out var totalBytes))
            return OnnxModelSize.Format(totalBytes, culture);

        return "未下载";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
