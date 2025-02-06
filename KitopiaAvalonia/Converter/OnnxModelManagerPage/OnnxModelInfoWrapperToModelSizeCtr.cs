using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Core.SDKs.Services.Plugin;
using PluginCore.Onnx;

namespace KitopiaAvalonia.Converter.OnnxModelManagerPage;

public class OnnxModelInfoWrapperToModelSizeCtr : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is OnnxModelInfoWrapper onnxModelInfoWrapper)
        {
            var path = PluginManager.GetPluginByPlgStr(onnxModelInfoWrapper.PluginStr).Path;
            var fileInfo = new FileInfo($"{path}{onnxModelInfoWrapper.Model.ModelPath}");
            if (fileInfo.Exists)
            {
                if (fileInfo.Length>1024*1024 * 1024)  
                {
                    return $"{Math.Round((double)fileInfo.Length / (1024 * 1024 * 1024), 2)}GB";
                }else if (fileInfo.Length > 1024 * 1024)
                {
                    return $"{Math.Round((double)fileInfo.Length / (1024 * 1024), 2)}MB";
                }
                else
                {
                    return $"{Math.Round((double)fileInfo.Length / 1024, 2)}KB";
                }
                
            }
        }

        return "0B";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}