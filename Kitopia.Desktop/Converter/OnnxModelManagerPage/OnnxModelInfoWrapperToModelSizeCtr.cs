using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class OnnxModelInfoWrapperToModelSizeCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OnnxModelInfoWrapper wrapper)
        {
            var modelFiles = new[] { wrapper.Model.ModelPath }
                .Concat(wrapper.Model.RequiredFiles)
                .Select(path => new FileInfo(path))
                .ToArray();
            if (modelFiles.All(file => file.Exists))
            {
                var length = modelFiles.Sum(file => file.Length);
                if (length > 1024 * 1024 * 1024)
                    return $"{Math.Round((double)length / (1024 * 1024 * 1024), 2)}GB";
                else if (length > 1024 * 1024)
                    return $"{Math.Round((double)length / (1024 * 1024), 2)}MB";
                else
                    return $"{Math.Round((double)length / 1024, 2)}KB";
            }
        }

        return "未下载";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
