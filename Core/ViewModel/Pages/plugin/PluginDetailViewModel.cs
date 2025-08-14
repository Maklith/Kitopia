using CommunityToolkit.Mvvm.ComponentModel;
using PluginInfoUiHelper = Core.Services.Plugin.PluginInfoUiHelper;

namespace Core.ViewModel.Pages.plugin;

public partial class PluginDetailViewModel : ObservableObject
{
    public PluginInfoUiHelper? PluginInfo { get; init; }

    public PluginDetailViewModel(PluginInfoUiHelper pluginStr)
    {
        PluginInfo = pluginStr;
    }
}