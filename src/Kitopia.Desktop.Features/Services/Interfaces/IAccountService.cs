using Kitopia.Desktop.Features.Services.Account;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IAccountService
{
    bool IsLoggedIn { get; }
    UserInfo? CurrentUser { get; }
    string? CurrentToken { get; }
    event Action<UserInfo?>? UserStateChanged;

    Task<bool> LoginWithTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> ExchangeCodeAndLoginAsync(string code, string? state = null, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<bool> RefreshUserInfoAsync(CancellationToken cancellationToken = default);
    void OpenBrowserLogin();
    Task InitializeAsync();
}
