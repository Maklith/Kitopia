using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Controls.Notifications;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Features.Services.Account;

public sealed class AccountService : IAccountService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<AccountService>();
    private static readonly HttpClient DefaultHttpClient = new(new HttpClientHandler
    {
#if DEBUG
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
        {
            if (message.RequestUri?.IsLoopback == true)
            {
                return true;
            }
            return errors == System.Net.Security.SslPolicyErrors.None;
        }
#endif
    });

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private sealed record PendingOAuthState(string CodeVerifier, DateTimeOffset CreatedAt);
    private readonly ConcurrentDictionary<string, PendingOAuthState> _pendingStates = new(StringComparer.Ordinal);

    public bool IsLoggedIn => CurrentUser != null && !string.IsNullOrWhiteSpace(CurrentToken);
    public UserInfo? CurrentUser { get; private set; }
    public string? CurrentToken { get; private set; }

    public event Action<UserInfo?>? UserStateChanged;

    public AccountService() : this(null)
    {
    }

    internal AccountService(HttpClient? httpClient)
    {
        _httpClient = httpClient ?? DefaultHttpClient;
    }

    public async Task InitializeAsync()
    {
        var savedToken = ConfigManger.Config?.userToken?.Trim();
        if (!string.IsNullOrWhiteSpace(savedToken))
        {
            Logger.Information("检测到已保存的用户凭证，正在验证登录状态...");
            await LoginWithTokenInternalAsync(savedToken, isSilent: true);
        }
    }

    public async Task<bool> LoginWithTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await LoginWithTokenInternalAsync(token, isSilent: false, cancellationToken);
    }

    private static void SaveConfigSafe()
    {
        try
        {
            ConfigManger.Save("KitopiaConfig");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "保存配置文件 KitopiaConfig 失败");
        }
    }

    private void ClearLocalSession(bool showToast = false, string? toastTitle = null, string? toastMessage = null, NotificationType toastType = NotificationType.Information)
    {
        CurrentToken = null;
        CurrentUser = null;
        if (ConfigManger.Config != null)
        {
            ConfigManger.Config.userToken = string.Empty;
            SaveConfigSafe();
        }

        if (File.Exists(KitopiaPaths.UserAvatarPath))
        {
            try
            {
                File.Delete(KitopiaPaths.UserAvatarPath);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "删除本地头像缓存失败");
            }
        }

        UserStateChanged?.Invoke(null);

        if (showToast && !string.IsNullOrWhiteSpace(toastTitle))
        {
            var toastService = ServiceManager.Services?.GetService<IToastService>();
            toastService?.Show(toastTitle, toastMessage ?? string.Empty, toastType);
        }
    }

    private async Task<bool> LoginWithTokenInternalAsync(string token, bool isSilent, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var result = await FetchUserInfoAsync(_httpClient, token, cancellationToken);
            if (result.User != null)
            {
                CurrentToken = token;
                CurrentUser = result.User;
                ConfigManger.Config.userToken = token;
                SaveConfigSafe();

                await CacheAvatarAsync(_httpClient, result.User, token, cancellationToken);

                UserStateChanged?.Invoke(CurrentUser);

                if (!isSilent)
                {
                    var toastService = ServiceManager.Services?.GetService<IToastService>();
                    toastService?.Show("登录成功", $"欢迎回来，{result.User.DisplayName}！", NotificationType.Success);
                }

                Logger.Information("用户登录成功: {UserName} ({DisplayName})", result.User.UserName, result.User.DisplayName);
                return true;
            }

            if (result.IsNetworkError)
            {
                // 网络异常或离线环境：绝不清除本地已保存凭据，保留离线登录态
                CurrentToken = token;
                CurrentUser ??= new UserInfo
                {
                    UserName = "离线用户",
                    Nickname = "离线账户"
                };

                if (File.Exists(KitopiaPaths.UserAvatarPath))
                {
                    try
                    {
                        CurrentUser.AvatarBytes = await File.ReadAllBytesAsync(KitopiaPaths.UserAvatarPath, cancellationToken);
                        CurrentUser.AvatarLocalPath = KitopiaPaths.UserAvatarPath;
                    }
                    catch
                    {
                        // ignore
                    }
                }

                UserStateChanged?.Invoke(CurrentUser);

                if (!isSilent)
                {
                    var toastService = ServiceManager.Services?.GetService<IToastService>();
                    toastService?.Show("网络未连接", "无法连接到认证服务器，已保留离线登录状态。", NotificationType.Warning);
                }

                Logger.Warning("用户网络未连接，已保留本地登录凭证与离线状态");
                return true;
            }

            // 仅在服务器明确拒绝（401 Unauthorized / Flag=false）时清除凭据
            ClearLocalSession(showToast: true, toastTitle: "登录失效", toastMessage: "登录凭据无效或已过期，请重新登录。", toastType: NotificationType.Error);
            Logger.Warning("用户登录失败：令牌无效已自动清除");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "登录验证过程中发生异常");
            if (!isSilent)
            {
                var toastService = ServiceManager.Services?.GetService<IToastService>();
                toastService?.Show("登录错误", $"登录过程中发生错误: {ex.Message}", NotificationType.Error);
            }
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var token = CurrentToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                        {
                            ["token"] = token,
                            ["client_id"] = "kitopia-desktop"
                        });
                        using var response = await _httpClient.PostAsync($"{ConfigManger.ApiUrl}/api/v1/oauth/revoke", content, cancellationToken);
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                            Logger.Warning("通知服务端撤销令牌未成功: {StatusCode}, {Body}", response.StatusCode, errorBody);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "向服务器通知撤销令牌时出现异常");
                    }
                }, cancellationToken);
            }

            ClearLocalSession(showToast: true, toastTitle: "已退出登录", toastMessage: "您的 Kitopia 账户已安全退出。", toastType: NotificationType.Information);
            Logger.Information("用户已注销登录");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RefreshUserInfoAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (string.IsNullOrWhiteSpace(CurrentToken))
            {
                return false;
            }

            var result = await FetchUserInfoAsync(_httpClient, CurrentToken, cancellationToken);
            if (result.User != null)
            {
                CurrentUser = result.User;
                await CacheAvatarAsync(_httpClient, result.User, CurrentToken, cancellationToken);
                UserStateChanged?.Invoke(CurrentUser);
                return true;
            }

            if (result.IsUnauthorized)
            {
                Logger.Warning("用户凭据已失效，已自动退出登录并清除本地凭证");
                ClearLocalSession(showToast: true, toastTitle: "登录失效", toastMessage: "登录凭据无效或已过期，请重新登录。", toastType: NotificationType.Error);
                return false;
            }

            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void OpenBrowserLogin()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);
        var codeVerifier = Base64UrlEncode(verifierBytes);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlEncode(challengeBytes);
        var stateBytes = RandomNumberGenerator.GetBytes(16);
        var state = Base64UrlEncode(stateBytes);

        var now = DateTimeOffset.UtcNow;
        // 清理超时的待验证状态（超过 5 分钟）
        foreach (var kv in _pendingStates)
        {
            if (now - kv.Value.CreatedAt > TimeSpan.FromMinutes(5))
            {
                _pendingStates.TryRemove(kv.Key, out _);
            }
        }
        _pendingStates[state] = new PendingOAuthState(codeVerifier, now);

        var redirectUri = "kitopiaurl://action=Login";
        var authUrl = $"{ConfigManger.WebUrl}/oauth/authorize?response_type=code&client_id=kitopia-desktop&redirect_uri={Uri.EscapeDataString(redirectUri)}&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256&state={Uri.EscapeDataString(state)}&scope=profile%20plugin:download_self";

        try
        {
            var desktopShell = ServiceManager.Services?.GetService<IDesktopShell>();
            if (desktopShell != null)
            {
                desktopShell.Open(authUrl);
            }
            else
            {
                Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });
            }
            Logger.Information("已在浏览器中打开 OAuth 授权页面: {Url}", authUrl);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "打开默认浏览器失败: {Url}", authUrl);
            var toastService = ServiceManager.Services?.GetService<IToastService>();
            toastService?.Show("无法打开浏览器", $"请手动访问: {authUrl}", NotificationType.Warning);
        }
    }

    public async Task<bool> ExchangeCodeAndLoginAsync(string code, string? state = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        code = code.Trim().TrimEnd('/');
        state = state?.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(state) || !_pendingStates.TryRemove(state, out var pendingState))
        {
            Logger.Warning("OAuth state 验证失败或未由本客户端发起授权请求，可能存在 CSRF 风险。实际: {Actual}", state);
            return false;
        }

        if (DateTimeOffset.UtcNow - pendingState.CreatedAt > TimeSpan.FromMinutes(5))
        {
            Logger.Warning("OAuth 授权请求已超时");
            return false;
        }

        var verifier = pendingState.CodeVerifier;
        if (string.IsNullOrWhiteSpace(verifier))
        {
            Logger.Warning("缺少必要的 code_verifier，拒绝换票");
            return false;
        }

        try
        {
            var payload = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "kitopia-desktop",
                ["code"] = code,
                ["redirect_uri"] = "kitopiaurl://action=Login"
            };
            if (!string.IsNullOrWhiteSpace(verifier))
            {
                payload["code_verifier"] = verifier;
            }

            using var content = new FormUrlEncodedContent(payload);
            using var response = await _httpClient.PostAsync($"{ConfigManger.ApiUrl}/api/v1/oauth/token", content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                Logger.Warning("OAuth 交换令牌失败: {StatusCode}, Body: {Body}", response.StatusCode, errorBody);
                return false;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
            {
                var accessToken = tokenProp.GetString();
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    return await LoginWithTokenInternalAsync(accessToken, isSilent: false, cancellationToken);
                }
            }

            Logger.Warning("OAuth 响应中未包含合法的 access_token: {Json}", json);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "OAuth 交换令牌过程中发生异常");
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record UserFetchResult(UserInfo? User, bool IsUnauthorized, bool IsNetworkError);

    private static async Task<UserFetchResult> FetchUserInfoAsync(HttpClient httpClient, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ConfigManger.ApiUrl}/api/v1/user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                Logger.Warning("用户凭据已失效 (401 Unauthorized)");
                return new UserFetchResult(null, IsUnauthorized: true, IsNetworkError: false);
            }

            if (!response.IsSuccessStatusCode)
            {
                Logger.Warning("请求用户信息接口返回非成功状态码: {StatusCode}", response.StatusCode);
                return new UserFetchResult(null, IsUnauthorized: false, IsNetworkError: true);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("flag", out var flagProp) || !flagProp.GetBoolean())
            {
                return new UserFetchResult(null, IsUnauthorized: true, IsNetworkError: false);
            }

            if (!root.TryGetProperty("data", out var dataProp) || dataProp.ValueKind != JsonValueKind.Object)
            {
                return new UserFetchResult(null, IsUnauthorized: false, IsNetworkError: false);
            }

            var userInfo = new UserInfo
            {
                UserName = dataProp.TryGetProperty("userName", out var u) ? u.GetString() ?? string.Empty : string.Empty,
                Nickname = dataProp.TryGetProperty("nickname", out var n) ? n.GetString() ?? string.Empty : string.Empty,
                UserMobile = dataProp.TryGetProperty("userMobile", out var m) ? m.GetString() : null,
                UserEmail = dataProp.TryGetProperty("userEmail", out var e) ? e.GetString() : null,
                State = dataProp.TryGetProperty("state", out var s) && s.TryGetInt32(out var stateVal) ? stateVal : 0,
                CreateTime = dataProp.TryGetProperty("createTime", out var c) && c.TryGetDateTime(out var dt) ? dt : null
            };

            if (dataProp.TryGetProperty("roles", out var rolesProp) && rolesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in rolesProp.EnumerateArray())
                {
                    if (item.GetString() is { } role)
                    {
                        userInfo.Roles.Add(role);
                    }
                }
            }

            return new UserFetchResult(userInfo, IsUnauthorized: false, IsNetworkError: false);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "从服务器拉取用户信息时发生网络异常");
            return new UserFetchResult(null, IsUnauthorized: false, IsNetworkError: true);
        }
    }

    private static async Task CacheAvatarAsync(HttpClient httpClient, UserInfo user, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{ConfigManger.ApiUrl}/api/v1/user/avatar");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length > 0)
                {
                    user.AvatarBytes = bytes;
                    try
                    {
                        await File.WriteAllBytesAsync(KitopiaPaths.UserAvatarPath, bytes, cancellationToken);
                        user.AvatarLocalPath = KitopiaPaths.UserAvatarPath;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "保存用户头像到本地磁盘失败");
                    }
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(user.UserName))
            {
                var bytes = await PluginNetworkService.GetAuthorAvatarBytesAsync(user.UserName, cancellationToken);
                if (bytes != null && bytes.Length > 0)
                {
                    user.AvatarBytes = bytes;
                    try
                    {
                        await File.WriteAllBytesAsync(KitopiaPaths.UserAvatarPath, bytes, cancellationToken);
                        user.AvatarLocalPath = KitopiaPaths.UserAvatarPath;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "保存备用头像到本地磁盘失败");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "缓存用户头像失败");
        }
    }
}
