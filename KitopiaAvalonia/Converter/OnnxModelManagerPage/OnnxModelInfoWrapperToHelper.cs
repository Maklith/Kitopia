using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.ViewModel.Pages;
using PluginCore.Onnx;

namespace KitopiaAvalonia.Converter.OnnxModelManagerPage;

public class OnnxModelInfoWrapperToHelper : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OnnxModelInfoWrapper wrapper)
        {
            return null;
        }

        var onnxModelRuntimeChangerHelpers = PluginOverall.AllTargetDevices.Select(e => new OnnxModelRuntimeChangerHelper()
        {
            CurrentDevice = ConfigManger.Config.OnnxTargetDevices.ContainsKey(wrapper.Model.SignName)
                ? ConfigManger.Config.OnnxTargetDevices[wrapper.Model.SignName]
                : "CPU",
            TargetDevice = e,
            OnnxModelInfoWrapper = wrapper
        }).ToList();
        return onnxModelRuntimeChangerHelpers;

    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}