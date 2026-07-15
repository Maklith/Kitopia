using CommunityToolkit.Mvvm.ComponentModel;
using PluginInfoUiHelper = Kitopia.Desktop.Features.Services.Plugin.PluginInfoUiHelper;

namespace Kitopia.Desktop.Features.ViewModel.Pages.plugin;

public class PluginDetailViewModel : ObservableObject
{
    public PluginInfoUiHelper? PluginInfo { get; init; }

    public PluginDetailViewModel(PluginInfoUiHelper pluginStr)
    {
        PluginInfo = pluginStr;
    }
}
