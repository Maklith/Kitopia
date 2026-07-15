using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class OnnxModelInfoWrapperToModelSizeCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string modelPath)
        {
            var fileInfo = new FileInfo(modelPath);
            if (fileInfo.Exists)
            {
                if (fileInfo.Length > 1024 * 1024 * 1024)
                    return $"{Math.Round((double)fileInfo.Length / (1024 * 1024 * 1024), 2)}GB";
                else if (fileInfo.Length > 1024 * 1024)
                    return $"{Math.Round((double)fileInfo.Length / (1024 * 1024), 2)}MB";
                else
                    return $"{Math.Round((double)fileInfo.Length / 1024, 2)}KB";
            }
        }

        return "未下载";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}