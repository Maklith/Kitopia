using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.Services.Account;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Abstractions.Shell;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.ViewModel.Account;

public partial class AccountCardViewModel : ObservableObject, IDisposable
{
    private readonly IAccountService _accountService;
    private readonly Action<Action> _dispatcher;

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string _displayName = "未登录";

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _primaryRole = string.Empty;

    [ObservableProperty]
    private string _avatarInitial = "U";

    [ObservableProperty]
    private Bitmap? _avatarBitmap;

    public AccountCardViewModel(IAccountService accountService, Action<Action>? dispatcher = null)
    {
        _accountService = accountService;
        _dispatcher = dispatcher ?? DefaultDispatch;
        _accountService.UserStateChanged += OnUserStateChanged;
        UpdateFromUser(_accountService.CurrentUser);
    }

    private static void DefaultDispatch(Action action)
    {
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(action);
    }

    private void OnUserStateChanged(UserInfo? user)
    {
        _dispatcher(() => UpdateFromUser(user));
    }

    private void UpdateFromUser(UserInfo? user)
    {
        IsLoggedIn = user != null;
        if (IsLoggedIn)
        {
            IsLoggingIn = false;
        }

        DisplayName = user?.DisplayName ?? "未登录";
        UserName = user?.UserName ?? string.Empty;
        Email = user?.UserEmail ?? string.Empty;
        PrimaryRole = user?.PrimaryRole ?? string.Empty;

        if (user != null)
        {
            var name = !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName : UserName;
            AvatarInitial = !string.IsNullOrWhiteSpace(name) ? name.Substring(0, 1).ToUpperInvariant() : "U";
        }
        else
        {
            AvatarInitial = "U";
        }

        Bitmap? newBitmap = null;
        if (user?.AvatarBytes is { Length: > 0 })
        {
            try
            {
                newBitmap = new Bitmap(new MemoryStream(user.AvatarBytes));
            }
            catch
            {
                newBitmap = null;
            }
        }
        else if (!string.IsNullOrWhiteSpace(user?.AvatarLocalPath) && File.Exists(user.AvatarLocalPath))
        {
            try
            {
                var bytes = File.ReadAllBytes(user.AvatarLocalPath);
                newBitmap = new Bitmap(new MemoryStream(bytes));
            }
            catch
            {
                newBitmap = null;
            }
        }

        var oldBitmap = AvatarBitmap;
        AvatarBitmap = newBitmap;
        oldBitmap?.Dispose();
    }

    [RelayCommand]
    public void Login()
    {
        IsLoggingIn = true;
        _accountService.OpenBrowserLogin();
    }

    [RelayCommand]
    public void CancelLogin()
    {
        IsLoggingIn = false;
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        await _accountService.LogoutAsync();
        UpdateFromUser(_accountService.CurrentUser);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _accountService.RefreshUserInfoAsync();
    }

    [RelayCommand]
    public void OpenWebAccount()
    {
        var url = $"{ConfigManger.WebUrl}/account";
        var desktopShell = ServiceManager.Services.GetService<IDesktopShell>();
        desktopShell?.Open(url);
    }

    public void Dispose()
    {
        _accountService.UserStateChanged -= OnUserStateChanged;
        var old = AvatarBitmap;
        AvatarBitmap = null;
        old?.Dispose();
    }
}
