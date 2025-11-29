#region

using System.Collections.ObjectModel;
using System.Windows.Input;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.Services;
using PluginCore;

#endregion

namespace Core.ViewModel.Main;

/// <summary>
/// 主窗口视图模型 / Main window view model for handling navigation and UI state
/// </summary>
public partial class MainWindowViewModel : ObservableRecipient
{
    public MainWindowViewModel()
    {
        WeakReferenceMessenger.Default.Register<MainWindowViewModel, PageChangeEventArgs>(this, OnNavigation);
    }

    [ObservableProperty] private bool _settingPage = false;
    [ObservableProperty] private string _version = "0.0.2.128";
    private void OnNavigation(MainWindowViewModel recipient, PageChangeEventArgs message)
    {
        Content = message.Key;
        SettingPage = message.Key == "Setting";
    }

    [ObservableProperty] private object? _content;

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new()
    {
        new MenuItemViewModel
        {
            MenuHeader = "主页",
            Key = "Home",
            MenuIconGlyph = "\uf481",
            MenuIconFilledGlyph = "\uf488"
        },
        new MenuItemViewModel
        {
            MenuHeader = "市场",
            Key = "Market",
            MenuIconGlyph = "\uf151",
            MenuIconFilledGlyph = "\uf151"
        },
        new MenuItemViewModel
        {
            MenuHeader = "插件",
            Key = "Plugin",
            MenuIconGlyph = "\uf60a",
            MenuIconFilledGlyph = "\uf614"
        },
        new MenuItemViewModel
        {
            MenuHeader = "情景",
            Key = "Scenario",
            MenuIconGlyph = "\ue065",
            MenuIconFilledGlyph = "\ue065"
        },
        new MenuItemViewModel
        {
            MenuHeader = "快捷键",
            Key = "Hotkey",
            MenuIconGlyph = "\uf4b9",
            MenuIconFilledGlyph = "\uf4c3"
        },
        new MenuItemViewModel
        {
            MenuHeader = "模型列表",
            Key = "OnnxModelManagerPage",
            MenuIconGlyph = "\uf83b",
            MenuIconFilledGlyph = "\uf853"
        }
    };

    [RelayCommand]
    public void ActivateSettingPage()
    {
        WeakReferenceMessenger.Default.Send<PageChangeEventArgs>(new PageChangeEventArgs("Setting"));
    }

    [RelayCommand]
    public void Exit()
    {
        ServiceManager.Services.GetService<IApplicationService>().Stop();
    }
}

/// <summary>
/// 页面切换事件参数 / Page change event arguments for navigation
/// </summary>
public class PageChangeEventArgs
{
    /// <summary>
    /// 初始化新的页面切换事件参数实例 / Initializes a new instance of PageChangeEventArgs
    /// </summary>
    /// <param name="key">页面键 / The page key</param>
    public PageChangeEventArgs(string key)
    {
        Key = key;
    }

    /// <summary>获取或设置页面键 / Gets or sets the page key</summary>
    public string Key { get; set; }
}

public partial class MenuItemViewModel : ObservableObject
{
    public string MenuHeader { get; set; }
    public string MenuIconGlyph { get; set; }
    public string MenuIconFilledGlyph { get; set; }

    public string Key { get; set; }
    public bool IsSeparator { get; set; }

    public ObservableCollection<MenuItemViewModel> Children { get; set; } = new();
    public ICommand ActivateCommand { get; set; }

    [ObservableProperty] private bool _isSelected = false;

    public MenuItemViewModel()
    {
        WeakReferenceMessenger.Default.Register<PageChangeEventArgs>(this,
            (recipient, message) => { IsSelected = message.Key == Key; });
        ActivateCommand = new RelayCommand(OnActivate);
    }

    private void OnActivate()
    {
        if (IsSeparator) return;
        WeakReferenceMessenger.Default.Send<PageChangeEventArgs>(new PageChangeEventArgs(Key));
    }
}