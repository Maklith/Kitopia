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

public partial class MainWindowViewModel : ObservableRecipient
{
    public MainWindowViewModel()
    {
        WeakReferenceMessenger.Default.Register<MainWindowViewModel, PageChangeEventArgs>(this, OnNavigation);
    }

    [ObservableProperty] private bool _settingPage = false;

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

public class PageChangeEventArgs
{
    public PageChangeEventArgs(string key)
    {
        Key = key;
    }

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