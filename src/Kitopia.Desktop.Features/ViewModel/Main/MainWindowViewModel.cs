#region

using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Feature.Localization;
using Kitopia.Feature.DeviceCommunication.Application;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

#endregion

using Avalonia.Media.Imaging;
using Kitopia.Desktop.Features.Services.Account;

namespace Kitopia.Desktop.Features.ViewModel.Main;

/// <summary>
/// 主窗口视图模型 / Main window view model for handling navigation and UI state
/// </summary>
public partial class MainWindowViewModel : ObservableRecipient
{
    private readonly INavigationService _navigationService;
    private readonly IAccountService? _accountService;

    [ObservableProperty] private object? _content;
    [ObservableProperty] private bool _settingPage;

    [ObservableProperty] private bool _isUserLoggedIn;
    [ObservableProperty] private string _userAvatarTooltip = Lang.Get("lang.kitopia.sign_in_account");
    [ObservableProperty] private string _userAvatarInitial = "U";
    [ObservableProperty] private Bitmap? _userAvatarBitmap;

    public MainWindowViewModel(INavigationService navigationService, IAccountService? accountService = null)
    {
        _navigationService = navigationService;
        _accountService = accountService;
        if (_accountService != null)
        {
            _accountService.UserStateChanged += OnUserStateChanged;
            UpdateAccountState(_accountService.CurrentUser);
        }

        _navigationService.PageNavigated += OnPageNavigated;
        Lang.Current.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(Lang.Language)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (var item in MenuItems) item.RefreshLanguage();
                UserAvatarTooltip = _accountService?.CurrentUser is { } user
                    ? Lang.Format("lang.kitopia.signed_in_value", user.DisplayName) : Lang.Get("lang.kitopia.sign_in_account");
            });
        };
        OnPageNavigated(_navigationService.CurrentPageRoute ?? "home");
    }

    private void OnUserStateChanged(UserInfo? user)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            UpdateAccountState(user);
        });
    }

    private void UpdateAccountState(UserInfo? user)
    {
        IsUserLoggedIn = user != null;
        UserAvatarTooltip = user != null ? Lang.Format("lang.kitopia.signed_in_value", user.DisplayName) : Lang.Get("lang.kitopia.sign_in_account");
        var name = user?.DisplayName ?? user?.UserName ?? "U";
        UserAvatarInitial = !string.IsNullOrWhiteSpace(name) ? name.Substring(0, 1).ToUpperInvariant() : "U";

        Bitmap? newBitmap = null;
        if (user?.AvatarBytes is { Length: > 0 })
        {
            try
            {
                newBitmap = new Bitmap(new System.IO.MemoryStream(user.AvatarBytes));
            }
            catch
            {
                newBitmap = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(user?.AvatarLocalPath) && System.IO.File.Exists(user.AvatarLocalPath))
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(user.AvatarLocalPath);
                newBitmap = new Bitmap(new System.IO.MemoryStream(bytes));
            }
            catch
            {
                newBitmap = null;
            }
        }

        var oldBitmap = UserAvatarBitmap;
        UserAvatarBitmap = newBitmap;
        oldBitmap?.Dispose();
    }

    public ObservableCollection<MenuItemViewModel> MenuItems { get; } = new()
    {
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.home",
            Key = "home",
            MenuIconGlyph = "\uf481",
            MenuIconFilledGlyph = "\uf488"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.marketplace",
            Key = "market",
            MenuIconGlyph = "\uf151",
            MenuIconFilledGlyph = "\uf151"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.plugins",
            Key = "plugin",
            MenuIconGlyph = "\uf60a",
            MenuIconFilledGlyph = "\uf614"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.scenarios",
            Key = "scenario",
            MenuIconGlyph = "\ue065",
            MenuIconFilledGlyph = "\ue065"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.hotkeys",
            Key = "hotkey",
            MenuIconGlyph = "\uf4b9",
            MenuIconFilledGlyph = "\uf4c3"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.models",
            Key = "onnx/model-manager",
            MenuIconGlyph = "\uf83b",
            MenuIconFilledGlyph = "\uf853"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.device_chat",
            Key = "device/chat",
            MenuIconGlyph = "\ue975",
            MenuIconFilledGlyph = "\ue975"
        },
        new MenuItemViewModel
        {
            MenuHeader = "lang.kitopia.index_status",
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

        if (!string.Equals(route, "device/chat", StringComparison.Ordinal))
        {
            try
            {
                var messageAppService = ServiceManager.Services?.GetService<IMessageAppService>();
                messageAppService?.UpdateDisplayContext(false, false, null);
            }
            catch
            {
            }
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
    public void ActivateAccountPage()
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

    private string _menuHeader = string.Empty;
    public string MenuHeader
    {
        get => Lang.Get(_menuHeader);
        set { _menuHeader = value; OnPropertyChanged(); }
    }
    internal void RefreshLanguage() => OnPropertyChanged(nameof(MenuHeader));
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
