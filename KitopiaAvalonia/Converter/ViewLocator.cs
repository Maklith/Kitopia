using System;
using System.Globalization;
using System.Threading.Tasks;
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
    private string? _currentPageKey;
    private UserControl? _currentPage;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var targetKey = value as string ?? "Home";

        if (_currentPage is not null && string.Equals(_currentPageKey, targetKey, StringComparison.Ordinal))
        {
            return _currentPage;
        }

        var nextPage = ResolvePage(targetKey);

        if (_currentPage is not null)
        {
            DisposePage(_currentPage);
        }

        _currentPageKey = targetKey;
        _currentPage = nextPage;

        return nextPage;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private static UserControl? ResolvePage(string args)
    {
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
            case "DeviceChat":
                return ServiceManager.Services.GetKeyedService<UserControl>("DeviceChatPage");
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

    private static void DisposePage(UserControl page)
    {
        var dataContext = page.DataContext;

        TryDispose(dataContext);

        if (!ReferenceEquals(page, dataContext))
        {
            TryDispose(page);
        }
    }

    private static void TryDispose(object? instance)
    {
        switch (instance)
        {
            case null:
                return;
            case IAsyncDisposable asyncDisposable:
                asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                return;
            case IDisposable disposable:
                disposable.Dispose();
                return;
        }
    }
}
