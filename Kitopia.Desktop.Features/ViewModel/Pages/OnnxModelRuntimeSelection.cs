using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Interfaces;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public sealed partial class OnnxModelRuntimeSelection : ObservableObject
{
    private readonly IConfigService _configService;

    public OnnxModelRuntimeSelection(OnnxModelInfoWrapper modelInfo, IReadOnlyList<OnnxRuntimeStatus> runtimes,
        IConfigService configService)
    {
        ModelInfo = modelInfo;
        Runtimes = runtimes;
        _configService = configService;
        _currentDevice = configService.Config.OnnxTargetDevices.GetValueOrDefault(modelInfo.Model.SignName, "CPU");
    }

    public OnnxModelInfoWrapper ModelInfo { get; }
    public IReadOnlyList<OnnxRuntimeStatus> Runtimes { get; }

    [ObservableProperty] private string _currentDevice;

    [RelayCommand]
    private void SelectRuntime(OnnxRuntimeStatus runtime)
    {
        if (!runtime.CanSelect || ModelInfo.Model.NeedDownload || !Runtimes.Contains(runtime)) return;
        CurrentDevice = runtime.Device;
    }

    partial void OnCurrentDeviceChanged(string value)
    {
        _configService.Config.OnnxTargetDevices[ModelInfo.Model.SignName] = value;
        _configService.Save("KitopiaConfig");
    }
}
