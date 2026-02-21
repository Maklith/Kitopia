using System;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;

namespace KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;

public partial class ScreenCaptureInfoSelfConnector : ObservableObject, PluginCore.CustomScenario.INodeInputConnector
{
    public StyleInclude Style =>
        new(new Uri("avares://KitopiaEx"))
        {
            Source = new Uri(
                "INodeInputConnector/ScreenCaptureInfoSelfConnector/ScreenCaptureInfoSelfConnectorStyle.axaml",
                UriKind.Relative)
        };

    public IDataTemplate IDataTemplate =>
        new ResourceInclude(new Uri("avares://KitopiaEx"))
            {
                Source = new Uri(
                    "INodeInputConnector/ScreenCaptureInfoSelfConnector/ScreenCaptureInfoSelfConnectorDataTemplate.axaml",
                    UriKind.Relative)
            }
            .TryGetResource("Template", null, out var variant)
            ? (IDataTemplate)variant
            : null;

    public ObservableValue Value { get; set; } = new()
    {
        Value = new CustomScenarioValue()
        {
            ShowType = typeof(ScreenCaptureInfoSelfConnector),
            SerializeType = typeof(ScreenCaptureInfo)
        }
    };

    [RelayCommand]
    private void GetScreenCaptureInfo()
    {
        ServiceManager.Services.GetService<IScreenCaptureWindow>()!.RequestUserSelectScreenInfo(e =>
        {
            Value.SetValue(e);
        });
       
    }
}