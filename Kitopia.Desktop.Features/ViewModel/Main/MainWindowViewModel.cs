#region

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Feature.DeviceCommunication.Application;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

#endregion

namespace Kitopia.Desktop.Features.ViewModel.Main;

/// <summary>
/// 主窗口视图模型 / Main window view model for handling navigation and UI state
/// </summary>
public partial class MainWindowViewModel : ObservableRecipient
{
    private readonly INavigationService _navigationService;
    [ObservableProperty] private object? _content;

    [ObservableProperty] private bool _settingPage;

    public MainWindowViewModel(INavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.PageNavigated += OnPageNavigated;
        OnPageNavigated(_navigationService.CurrentPageRoute ?? "home");
    }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new()
    {
        new MenuItemViewModel
        {
            MenuHeader = "主页",
            Key = "home",
            MenuIconGlyph = "\uf481",
            MenuIconFilledGlyph = "\uf488"
        },
        new MenuItemViewModel
        {
            MenuHeader = "市场",
            Key = "market",
            MenuIconGlyph = "\uf151",
            MenuIconFilledGlyph = "\uf151"
        },
        new MenuItemViewModel
        {
            MenuHeader = "插件",
            Key = "plugin",
            MenuIconGlyph = "\uf60a",
            MenuIconFilledGlyph = "\uf614"
        },
        new MenuItemViewModel
        {
            MenuHeader = "情景",
            Key = "scenario",
            MenuIconGlyph = "\ue065",
            MenuIconFilledGlyph = "\ue065"
        },
        new MenuItemViewModel
        {
            MenuHeader = "快捷键",
            Key = "hotkey",
            MenuIconGlyph = "\uf4b9",
            MenuIconFilledGlyph = "\uf4c3"
        },
        new MenuItemViewModel
        {
            MenuHeader = "模型列表",
            Key = "onnx/model-manager",
            MenuIconGlyph = "\uf83b",
            MenuIconFilledGlyph = "\uf853"
        },
        new MenuItemViewModel
        {
            MenuHeader = "设备聊天",
            Key = "device/chat",
            MenuIconGlyph = "\ue975",
            MenuIconFilledGlyph = "\ue975"
        },
        new MenuItemViewModel
        {
            MenuHeader = "\u7d22\u5f15\u72b6\u6001",
            Key = "index/status",
            MenuIconGlyph = "\uf105",
            MenuIconFilledGlyph = "\uf105"
        }
    };

    private void OnPageNavigated(string route)
    {
        Content = route;
        SettingPage = route == "settings";

        UpdateMenuSelection(MenuItems, route);

        try
        {
            var messageAppService = ServiceManager.Services?.GetService<IMessageAppService>();
            messageAppService?.UpdateDisplayContext(
                isMainWindowActive: true,
                isDeviceChatPageOpen: string.Equals(route, "device/chat", StringComparison.Ordinal),
                selectedConversationId: null);

        }
        catch
        {
        }
    }

    private static void UpdateMenuSelection(IEnumerable<MenuItemViewModel> items, string route)
    {
        foreach (var item in items)
        {
            item.IsSelected = string.Equals(item.Key, route, StringComparison.Ordinal);
            if (item.Children.Count > 0)
            {
                UpdateMenuSelection(item.Children, route);
            }
        }
    }

    [RelayCommand]
    public void ActivateSettingPage()
    {
        _navigationService.Navigate("settings");
    }

    [RelayCommand]
    public async Task Exit()
    {
        await ServiceManager.Services.GetService<IApplicationService>()!.StopAsync();
    }
}

public partial class MenuItemViewModel : ObservableObject
{
    private readonly INavigationService? _navigationService;
    [ObservableProperty] private bool _isSelected;

    public MenuItemViewModel()
    {
        _navigationService = ServiceManager.Services?.GetService(typeof(INavigationService)) as INavigationService;
        if (_navigationService is not null)
        {
            _navigationService.PageNavigated += route => { IsSelected = route == Key; };
            IsSelected = _navigationService.CurrentPageRoute == Key;
        }
        ActivateCommand = new RelayCommand(OnActivate);
    }

    public string MenuHeader { get; set; }
    public string MenuIconGlyph { get; set; }
    public string MenuIconFilledGlyph { get; set; }

    public string Key { get; set; }
    public bool IsSeparator { get; set; }

    public ObservableCollection<MenuItemViewModel> Children { get; set; } = new();
    public ICommand ActivateCommand { get; set; }

    private void OnActivate()
    {
        if (IsSeparator) return;
        var navigationService = _navigationService ?? ServiceManager.Services?.GetService<INavigationService>();
        navigationService?.Navigate(Key);
    }
}
