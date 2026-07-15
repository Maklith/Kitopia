using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.ViewModel.Pages;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Converter.OnnxModelManagerPage;

public class OnnxModelInfoWrapperToHelper : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not OnnxModelInfoWrapper wrapper) return null;

        var onnxModelRuntimeChangerHelpers = PluginOverall.AllTargetDevices.Select(e =>
            new OnnxModelRuntimeChangerHelper
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