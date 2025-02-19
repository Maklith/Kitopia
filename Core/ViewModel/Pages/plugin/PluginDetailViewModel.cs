using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;


namespace Core.ViewModel.Pages.plugin;

public partial class PluginDetailViewModel : ObservableObject
{
    public PluginInfoUiHelper? PluginInfo { get; init; }
    public PluginDetailViewModel(PluginInfoUiHelper pluginStr)
    {
        PluginInfo = pluginStr;
    }

   
}