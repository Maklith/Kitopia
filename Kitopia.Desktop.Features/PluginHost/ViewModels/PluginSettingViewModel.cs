#region

using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario.Attribute.ConfigField;

#endregion

namespace Kitopia.Desktop.Features.ViewModel.Pages.plugin;

public struct PluginSettingItem
{
    public string Title { get; set; }
    public string Key { get; set; }
}

public partial class PluginSettingViewModel : ObservableRecipient
{
    [ObservableProperty] private ObservableCollection<PluginSettingItem> _settingItems = new();
    [ObservableProperty] private string _pluginName = string.Empty;

    public void LoadByPluginInfo(string pluginInfo)
    {
        PluginName = $"选择{pluginInfo}配置文件";
        SettingItems.Clear();
        foreach (var (key, value) in ConfigManger.Configs)
            if (key.StartsWith(pluginInfo))
                SettingItems.Add(new PluginSettingItem
                {
                    Title = value.GetType().GetCustomAttribute<ConfigName>()?.Name ?? value.Name,
                    Key = value.Name
                });
    }

    [RelayCommand]
    public void Navigate(string na)
    {
        ServiceManager.Services?.GetService<INavigationService>()?.Navigate(
            "plugin/settings/detail",
            new Dictionary<string, object?>
            {
                ["configKey"] = na
            });
    }
}
