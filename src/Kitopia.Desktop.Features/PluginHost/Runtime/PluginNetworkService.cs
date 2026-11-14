using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginNetworkService
{
    private const string PluginApiPath = "api/v1/plugin";
    private static readonly TimeSpan PluginInfoCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AvatarCacheDuration = TimeSpan.FromDays(1);
    private static readonly ILogger Logger = LogManager.Logger.ForContext<PluginNetworkService>();
    private static readonly ConcurrentDictionary<string, CacheEntry<OnlinePluginInfo>> OnlinePluginInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CacheEntry<byte[]>> PluginAvatarCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CacheEntry<byte[]>> AuthorAvatarCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<OnlinePluginInfo?>> PendingOnlinePluginInfo = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> PendingPluginAvatars = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Task<byte[]?>> PendingAuthorAvatars = new(StringComparer.OrdinalIgnoreCase);

    internal static HttpRequestMessage CreateAuthorizedGetRequest(string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, GetPluginApiUrl(path));
        var token = ConfigManger.Config?.userToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    public static readonly HttpClient HttpClient = new(new HttpClientHandler
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
    })
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", $"Kitopia/{ConfigManger.Version}" }
        }
    };

    public static async Task<OnlinePluginInfo?> GetOnlinePluginInfo(
        string pluginSignName,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = NormalizeCacheKey(pluginSignName);
        if (OnlinePluginInfoCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow) return cached.Value;
            OnlinePluginInfoCache.TryRemove(cacheKey, out _);
        }

        var pending = PendingOnlinePluginInfo.GetOrAdd(
            cacheKey,
            _ => GetPluginDataAsync<OnlinePluginInfo>(Uri.EscapeDataString(pluginSignName), CancellationToken.None));
        var result = await pending.WaitAsync(cancellationToken);
        if (result is not null)
        {
            OnlinePluginInfoCache[cacheKey] = new CacheEntry<OnlinePluginInfo>(
                result,
                DateTimeOffset.UtcNow.Add(PluginInfoCacheDuration));
            PendingOnlinePluginInfo.TryRemove(new KeyValuePair<string, Task<OnlinePluginInfo?>>(cacheKey, pending));
        }
        else
        {
            PendingOnlinePluginInfo.TryRemove(new KeyValuePair<string, Task<OnlinePluginInfo?>>(cacheKey, pending));
        }

        return result;
    }

    public static async Task<PluginPage?> GetPluginsAsync(
        int page = 1,
        int pageSize = 12,
        string? query = null,
        string? platform = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}"
        };

        if (!string.IsNullOrWhiteSpace(query))
        {
            queryParams.Add($"query={Uri.EscapeDataString(query.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            queryParams.Add($"platform={Uri.EscapeDataString(platform.Trim().ToLowerInvariant())}");
        }

        var queryString = string.Join("&", queryParams);
        var pageResult = await GetPluginDataAsync<PluginPage>($"all?{queryString}", cancellationToken);
        if (pageResult?.Items is { Count: > 0 } items)
        {
            foreach (var plugin in items)
            {
                OnlinePluginInfoCache[NormalizeCacheKey(plugin.NameSign)] = new CacheEntry<OnlinePluginInfo>(
                    plugin,
                    DateTimeOffset.UtcNow.Add(PluginInfoCacheDuration));
            }
        }

        return pageResult;
    }

    internal static async Task<PluginPackage> DownloadPackageAsync(
        string pluginSignName, string version, CancellationToken cancellationToken = default,
        Action<long, long?>? reportProgress = null)
    {
        PluginDiscoveryService.ValidatePluginSign(pluginSignName);
        var staging = Path.Combine(KitopiaPaths.PluginsDirectory, $".staging-{Guid.NewGuid():N}");
        var archive = Path.Combine(KitopiaPaths.TempDirectory, $"{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadArchiveAsync(pluginSignName, version, archive, HttpClient, reportProgress, cancellationToken);
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, staging), cancellationToken);
            var package = new PluginPackage(staging, pluginSignName, version);
            var avatar = await GetAvatarBytesAsync(pluginSignName, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (avatar is not null) await File.WriteAllBytesAsync(Path.Combine(staging, "avatar.png"), avatar, cancellationToken);
            return package;
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            throw;
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    internal static async Task DownloadArchiveAsync(string pluginSignName, string version, string archive,
        HttpClient client, Action<long, long?>? reportProgress, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedGetRequest(
            $"download/{Uri.EscapeDataString(GetCurrentRuntimeIdentifier())}/{Uri.EscapeDataString(pluginSignName)}/{Uri.EscapeDataString(version)}");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        HandlePossibleUnauthorized(response.StatusCode);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        reportProgress?.Invoke(0, totalBytes);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        var buffer = new byte[81920];
        long downloadedBytes = 0;
        var lastReport = Stopwatch.GetTimestamp();
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;
            if (Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds < 100) continue;
            reportProgress?.Invoke(downloadedBytes, totalBytes);
            lastReport = Stopwatch.GetTimestamp();
        }
        reportProgress?.Invoke(downloadedBytes, totalBytes ?? downloadedBytes);
    }

    public static async Task<byte[]?> GetAvatarBytesAsync(
        string pluginSignName,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = NormalizeCacheKey(pluginSignName);
        if (PluginAvatarCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow) return cached.Value;
            PluginAvatarCache.TryRemove(cacheKey, out _);
        }

        var pending = PendingPluginAvatars.GetOrAdd(
            cacheKey,
            _ => FetchPluginAvatarAsync(pluginSignName, HttpClient));
        var result = await pending.WaitAsync(cancellationToken);
        if (result is { Length: > 0 })
        {
            PluginAvatarCache[cacheKey] = new CacheEntry<byte[]>(result, DateTimeOffset.UtcNow.Add(AvatarCacheDuration));
        }
        PendingPluginAvatars.TryRemove(new KeyValuePair<string, Task<byte[]?>>(cacheKey, pending));

        return result;
    }

    public static async Task<string?> GetAuthorNameAsync(int authorId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/v1/user/baseInfo"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", authorId.ToString());
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonConvert.DeserializeObject<PluginApiResponse<UserBaseInfo>>(content);
            return apiResponse is { Flag: true } ? apiResponse.Data?.UserName : null;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "获取作者信息错误");
            return null;
        }
    }

    public static async Task<byte[]?> GetAuthorAvatarBytesAsync(
        string userName,
        CancellationToken cancellationToken = default)
    {
        var normalizedUserName = userName.Trim();
        var cacheKey = NormalizeCacheKey(normalizedUserName);
        if (string.IsNullOrWhiteSpace(cacheKey)) return null;
        if (AuthorAvatarCache.TryGetValue(cacheKey, out var cached))
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow) return cached.Value;
            AuthorAvatarCache.TryRemove(cacheKey, out _);
        }

        var pending = PendingAuthorAvatars.GetOrAdd(
            cacheKey,
            _ => FetchAuthorAvatarAsync(normalizedUserName));
        var result = await pending.WaitAsync(cancellationToken);
        if (result is { Length: > 0 })
        {
            AuthorAvatarCache[cacheKey] = new CacheEntry<byte[]>(result, DateTimeOffset.UtcNow.Add(AvatarCacheDuration));
        }
        PendingAuthorAvatars.TryRemove(new KeyValuePair<string, Task<byte[]?>>(cacheKey, pending));

        return result;
    }

    internal static async Task<byte[]?> FetchPluginAvatarAsync(string pluginSignName, HttpClient client)
    {
        try
        {
            using var request = CreateAuthorizedGetRequest($"avatar?namesign={Uri.EscapeDataString(pluginSignName)}");
            using var response = await client.SendAsync(request, CancellationToken.None);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                HandlePossibleUnauthorized(response.StatusCode);
                Logger.Warning("插件图标请求失败: {StatusCode} {Plugin}", response.StatusCode, pluginSignName);
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "获取插件图标错误");
            return null;
        }
    }

    private static async Task<byte[]?> FetchAuthorAvatarAsync(string userName)
    {
        try
        {
            var url = $"{ConfigManger.ApiUrl}/api/v1/user/avatar/{Uri.EscapeDataString(userName)}";
            using var response = await HttpClient.GetAsync(url, CancellationToken.None);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsByteArrayAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "获取作者头像错误");
            return null;
        }
    }

    public static Task<string?> GetLatestVersionAsync(
        string pluginSignName,
        CancellationToken cancellationToken = default) =>
        GetLatestCompatibleVersionAsync(pluginSignName, GetCurrentRuntimeIdentifier(), HttpClient, cancellationToken);

    internal static async Task<string?> GetLatestCompatibleVersionAsync(
        string pluginSignName,
        string runtimeIdentifier,
        HttpClient client,
        CancellationToken cancellationToken = default)
    {
        var versions = await GetAvailableVersionsForRuntimeAsync(
            pluginSignName, runtimeIdentifier, client, cancellationToken);
        return SelectLatestCompatibleVersion(versions, runtimeIdentifier);
    }

    internal static string? SelectLatestCompatibleVersion(
        IEnumerable<VersionDetail>? versions,
        string? currentRuntimeIdentifier = null)
    {
        if (versions is null)
        {
            return null;
        }

        var rid = string.IsNullOrWhiteSpace(currentRuntimeIdentifier)
            ? GetCurrentRuntimeIdentifier()
            : currentRuntimeIdentifier;

        string? latest = null;
        foreach (var item in versions)
        {
            if (!item.CanDownload ||
                string.IsNullOrWhiteSpace(item.Version) ||
                !SupportsRuntimeIdentifier(item.AvailablePlatforms, rid))
            {
                continue;
            }

            if (latest is null || PluginDependencyService.IsVersionNewer(item.Version, latest))
            {
                latest = item.Version;
            }
        }

        return latest;
    }

    public static Task<List<VersionDetail>?> GetAvailableVersionsAsync(
        string pluginSignName,
        CancellationToken cancellationToken = default) =>
        GetAvailableVersionsForRuntimeAsync(pluginSignName, GetCurrentRuntimeIdentifier(), HttpClient, cancellationToken);

    private static Task<List<VersionDetail>?> GetAvailableVersionsForRuntimeAsync(
        string pluginSignName,
        string runtimeIdentifier,
        HttpClient client,
        CancellationToken cancellationToken) =>
        GetPluginDataAsync<List<VersionDetail>>(
            $"versions/{Uri.EscapeDataString(runtimeIdentifier)}/{Uri.EscapeDataString(pluginSignName)}",
            cancellationToken,
            client);

    public static async Task<List<VersionDetail>?> GetVersionDetailsAsync(
        string pluginSignName,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var history = await GetPluginDataAsync<List<VersionDetail>>(
                $"history/{Uri.EscapeDataString(pluginSignName)}",
                cancellationToken);
            if (history is { Count: > 0 })
            {
                return history;
            }

            version ??= await GetLatestVersionAsync(pluginSignName, cancellationToken);
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            var releases = await GetPluginDataAsync<List<VersionDetail>>(
                $"detail/{Uri.EscapeDataString(pluginSignName)}/{Uri.EscapeDataString(version)}?allBeforeThisVersion=true",
                cancellationToken);
            releases?.Reverse();
            return releases;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "获取版本详情错误");
            return null;
        }
    }

    private static async Task<T?> GetPluginDataAsync<T>(
        string path,
        CancellationToken cancellationToken,
        HttpClient? client = null)
    {
        try
        {
            using var request = CreateAuthorizedGetRequest(path);
            using var response = await (client ?? HttpClient).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                HandlePossibleUnauthorized(response.StatusCode);
                Logger.Warning("插件接口请求失败: {StatusCode} {Path}", response.StatusCode, path);
                return default;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonConvert.DeserializeObject<PluginApiResponse<T>>(content);
            return apiResponse is { Flag: true } ? apiResponse.Data : default;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "请求插件接口错误: {Path}", path);
            return default;
        }
    }

    private static void HandlePossibleUnauthorized(HttpStatusCode statusCode)
    {
        if (statusCode == HttpStatusCode.Unauthorized && !string.IsNullOrWhiteSpace(ConfigManger.Config?.userToken))
        {
            Logger.Warning("插件请求返回 401 Unauthorized，用户凭据已失效，触发账户自动刷新注销");
            var accountService = ServiceManager.Services.GetService<IAccountService>();
            if (accountService != null)
            {
                _ = Task.Run(async () => await accountService.RefreshUserInfoAsync());
            }
        }
    }

    private static string GetPluginApiUrl(string path) => $"{ConfigManger.ApiUrl}/{PluginApiPath}/{path}";

    private static string NormalizeCacheKey(string value) => value.Trim();

    private readonly record struct CacheEntry<T>(T Value, DateTimeOffset ExpiresAt);

    public static bool SupportsCurrentPlatform(IReadOnlyCollection<string>? availablePlatforms, bool allowEmpty = false) =>
        SupportsRuntimeIdentifier(availablePlatforms, GetCurrentRuntimeIdentifier(), allowEmpty);

    public static string GetCurrentRuntimeIdentifier() => GetRuntimeIdentifier(
        OperatingSystem.IsWindows(),
        OperatingSystem.IsMacOS(),
        OperatingSystem.IsLinux(),
        System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);

    internal static string GetRuntimeIdentifier(
        bool isWindows,
        bool isMacOS,
        bool isLinux,
        System.Runtime.InteropServices.Architecture architecture)
    {
        var arch = architecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64";
        if (isWindows)
        {
            return $"win-{arch}";
        }

        if (isMacOS)
        {
            return $"osx-{arch}";
        }

        return isLinux ? $"linux-{arch}" : $"linux-{arch}";
    }

    internal static string? NormalizePlatformIdentifier(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        return platform.Trim().ToLowerInvariant() switch
        {
            "1" or "win" or "windows" or "win-x64" or "windows-x64" => "win-x64",
            "4" or "win-arm64" or "windows-arm64" => "win-arm64",
            "2" or "osx" or "mac" or "macos" or "osx-x64" or "macos-x64" => "osx-x64",
            "5" or "osx-arm64" or "macos-arm64" => "osx-arm64",
            "3" or "linux" or "linux-x64" => "linux-x64",
            "6" or "linux-arm64" => "linux-arm64",
            var other => other
        };
    }

    internal static bool SupportsRuntimeIdentifier(
        IReadOnlyCollection<string>? platforms,
        string currentRuntimeIdentifier,
        bool allowEmpty = false)
    {
        if (platforms is null || platforms.Count == 0)
        {
            return allowEmpty;
        }

        var normalizedCurrent = NormalizePlatformIdentifier(currentRuntimeIdentifier)
            ?? currentRuntimeIdentifier.Trim().ToLowerInvariant();
        var hasEntries = false;
        foreach (var platform in platforms)
        {
            var normalized = NormalizePlatformIdentifier(platform);
            if (normalized is null)
            {
                continue;
            }

            hasEntries = true;
            if (string.Equals(normalized, normalizedCurrent, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return !hasEntries && allowEmpty;
    }
}
