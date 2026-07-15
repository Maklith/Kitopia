using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.Desktop.Features.Services.Config;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public partial class OnnxModelRuntimeChangerHelper : ObservableObject
{
    public string TargetDevice { get; set; }
    [ObservableProperty] public string currentDevice;

    partial void OnCurrentDeviceChanged(string value)
    {
        if (OnnxModelInfoWrapper is null) return;
        ConfigManger.Config.OnnxTargetDevices[OnnxModelInfoWrapper.Model.SignName] = value;
        ConfigManger.Save("KitopiaConfig");
    }

    public OnnxModelInfoWrapper OnnxModelInfoWrapper { get; set; }
}

public class OnnxModelManagerPageViewModel : ObservableObject
{
}