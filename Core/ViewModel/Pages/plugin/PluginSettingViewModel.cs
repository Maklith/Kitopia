#region

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using log4net;
using PluginCore;
using PluginCore.Attribute;

#endregion

namespace Core.ViewModel.Pages.plugin;

public struct PluginSettingItem
{
    public string Title { get; set; }
    public string Key { get; set; }
}

public partial class PluginSettingViewModel : ObservableRecipient
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(PluginSettingViewModel));
    [ObservableProperty] private ObservableCollection<PluginSettingItem> _settingItems = new();
    [ObservableProperty] private string _pluginName = string.Empty;

    public void LoadByPluginInfo(string pluginInfo)
    {
        PluginName = $"选择{pluginInfo}配置文件";
        SettingItems.Clear();
        foreach (var (key, value) in ConfigManger.Configs)
            if (key.StartsWith(pluginInfo))
                SettingItems.Add(new PluginSettingItem()
                {
                    Title = value.GetType().GetCustomAttribute<ConfigName>()?.Name ?? value.Name,
                    Key = value.Name
                });
    }

    [RelayCommand]
    public void Navigate(string na)
    {
        ((INavigationPageService)ServiceManager.Services!.GetService(typeof(INavigationPageService))).Navigate(
            $"PluginSetting_{na}");
    }
}