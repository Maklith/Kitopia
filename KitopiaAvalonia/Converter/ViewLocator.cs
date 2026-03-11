using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Core.Services.Config;
using Core.ViewModel.Pages.plugin;
using KitopiaAvalonia.Pages;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaAvalonia.Converter;

public class ViewLocator : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string args) return ServiceManager.Services.GetKeyedService<UserControl>("HomePage");

        switch (args)
        {
            case "Home":
                return ServiceManager.Services.GetKeyedService<UserControl>("HomePage");
            case "Market":
                return ServiceManager.Services.GetKeyedService<UserControl>("MarketPage");
            case "Plugin":
                return ServiceManager.Services.GetKeyedService<UserControl>("PluginManagerPage");
            case "Scenario":
                return ServiceManager.Services.GetKeyedService<UserControl>("CustomScenariosManagerPage");
            case "Hotkey":
                return ServiceManager.Services.GetKeyedService<UserControl>("HotKeyManagerPage");
            case "OnnxModelManagerPage":
                return ServiceManager.Services.GetKeyedService<UserControl>("OnnxModelManagerPage");
            case "DeviceDiscovery":
                return ServiceManager.Services.GetKeyedService<UserControl>("DeviceDiscoveryPage");
            case "Setting":
            {
                var settingPage = ServiceManager.Services.GetService<SettingPage>();
                settingPage.LoadAllConfigs();
                return settingPage;
            }
            default:
            {
                if (args.StartsWith("PluginSettingSelectPage_"))
                {
                    var keyedService = ServiceManager.Services.GetKeyedService<UserControl>("PluginSettingSelectPage");
                    ((PluginSettingViewModel)keyedService.DataContext).LoadByPluginInfo(args.Split("_", 2)[1]);
                    return keyedService;
                }

                if (args.StartsWith("PluginSetting_"))
                {
                    var settingPage = ServiceManager.Services.GetService<SettingPage>();
                    if (ConfigManger.Configs.TryGetValue(args.Split("_", 2)[1], out var config))
                        settingPage.ChangeConfig(config);

                    return settingPage;
                }

                return null;
            }
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}