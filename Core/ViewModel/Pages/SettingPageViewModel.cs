#region

using CommunityToolkit.Mvvm.ComponentModel;
using PluginCore.Config;

#endregion

namespace Core.ViewModel.Pages;

public class SettingPageViewModel : ObservableRecipient
{
    private ConfigBase _configBase;


    public SettingPageViewModel(ConfigBase configBase)
    {
        _configBase = configBase;
    }
}