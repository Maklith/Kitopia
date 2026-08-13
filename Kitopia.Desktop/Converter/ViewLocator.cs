using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.ViewModel.Pages.plugin;
using Kitopia.Desktop.Pages;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Converter;

public class ViewLocator : IValueConverter
{
    private string? _currentPageKey;
    private UserControl? _currentPage;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var targetKey = value as string ?? "home";

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
            case "home":
                return ServiceManager.Services.GetKeyedService<UserControl>("HomePage");
            case "market":
                return ServiceManager.Services.GetKeyedService<UserControl>("MarketPage");
            case "plugin":
                return ServiceManager.Services.GetKeyedService<UserControl>("PluginManagerPage");
            case "scenario":
                return ServiceManager.Services.GetKeyedService<UserControl>("CustomScenariosManagerPage");
            case "hotkey":
                return ServiceManager.Services.GetKeyedService<UserControl>("HotKeyManagerPage");
            case "onnx/model-manager":
                return ServiceManager.Services.GetKeyedService<UserControl>("OnnxModelManagerPage");
            case "index/status":
                return ServiceManager.Services.GetKeyedService<UserControl>("IndexStatusPage");
            case "device/chat":
                return ServiceManager.Services.GetKeyedService<UserControl>("DeviceChatPage");
            case "settings":
            {
                var settingPage = ServiceManager.Services.GetService<SettingPage>();
                settingPage.LoadAllConfigs();
                return settingPage;
            }
            default:
            {
                if (args.StartsWith("plugin/settings/select/"))
                {
                    var keyedService = ServiceManager.Services.GetKeyedService<UserControl>("PluginSettingSelectPage");
                    ((PluginSettingViewModel)keyedService.DataContext).LoadByPluginInfo(args["plugin/settings/select/".Length..]);
                    return keyedService;
                }

                if (args.StartsWith("plugin/settings/detail/"))
                {
                    var settingPage = ServiceManager.Services.GetService<SettingPage>();
                    if (ConfigManger.Configs.TryGetValue(args["plugin/settings/detail/".Length..], out var config))
                        settingPage.ChangeConfig(config);

                    return settingPage;
                }

                if (args.StartsWith("settings/field/", StringComparison.Ordinal))
                {
                    var settingPage = ServiceManager.Services.GetService<SettingPage>();
                    settingPage.LoadAllConfigs(args["settings/field/".Length..]);
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
