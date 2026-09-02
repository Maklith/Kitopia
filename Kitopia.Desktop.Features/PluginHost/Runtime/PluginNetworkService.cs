using System.IO.Compression;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Utils;
using Newtonsoft.Json;
using Serilog;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginNetworkService
{
    private const string PluginApiPath = "api/v1/plugin";
    private static readonly ILogger Logger = LogManager.Logger.ForContext<PluginNetworkService>();

    public static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", $"Kitopia/{ConfigManger.Version}" }
        }
    };

    public static Task<OnlinePluginInfo?> GetOnlinePluginInfo(
        string pluginSignName,
        CancellationToken cancellationToken = default) =>
        GetPluginDataAsync<OnlinePluginInfo>(
            Uri.EscapeDataString(pluginSignName),
            cancellationToken);

    public static Task<PluginPage?> GetPluginsAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GetPluginDataAsync<PluginPage>(
            $"all?page={page}&pageSize={pageSize}&platform={GetCurrentPlatformName()}",
            cancellationToken);

    public static async Task<bool> DownloadPlugin(
        string pluginSignName,
        string version,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.Debug("从服务器下载插件 {PluginSignName} 版本 {Version}", pluginSignName, version);
            var downloadPath = GetPluginApiUrl(
                $"download/{GetCurrentPlatformType()}/{Uri.EscapeDataString(pluginSignName)}/{Uri.EscapeDataString(version)}");
            using var response = await HttpClient.GetAsync(
                downloadPath,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var tempPath = Path.Combine(KitopiaPaths.TempDirectory, $"{Guid.NewGuid():N}.zip");
            try
            {
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await input.CopyToAsync(output, cancellationToken);
                }

                var pluginDirectory = KitopiaPaths.GetPluginDirectory(pluginSignName);
                Directory.CreateDirectory(pluginDirectory);
                ZipFile.ExtractToDirectory(tempPath, pluginDirectory, overwriteFiles: true);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }

            await DownloadAvatar(pluginSignName, cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "下载插件错误");
            return false;
        }
    }

    public static async Task<byte[]?> GetAvatarBytesAsync(
        string pluginSignName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetPluginDataAsync<byte[]>(
                $"avatar?namesign={Uri.EscapeDataString(pluginSignName)}",
                cancellationToken);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "获取插件图标错误");
            return null;
        }
    }

    private static async Task DownloadAvatar(string pluginSignName, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await GetAvatarBytesAsync(pluginSignName, cancellationToken);
            if (bytes is null)
            {
                return;
            }

            var pluginDirectory = KitopiaPaths.GetPluginDirectory(pluginSignName);
            Directory.CreateDirectory(pluginDirectory);
            await File.WriteAllBytesAsync(
                KitopiaPaths.GetPluginAvatarPath(pluginSignName),
                bytes,
                cancellationToken);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "下载插件图标错误");
        }
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

    public static async Task<string?> GetLatestVersionAsync(
        string pluginSignName,
        CancellationToken cancellationToken = default)
    {
        var plugin = await GetOnlinePluginInfo(pluginSignName, cancellationToken);
        return plugin?.LastVersion;
    }

    public static Task<List<VersionDetail>?> GetAvailableVersionsAsync(
        string pluginSignName,
        CancellationToken cancellationToken = default) =>
        GetPluginDataAsync<List<VersionDetail>>(
            $"versions/{GetCurrentPlatformType()}/{Uri.EscapeDataString(pluginSignName)}",
            cancellationToken);

    public static async Task<List<VersionDetail>?> GetVersionDetailsAsync(
        string pluginSignName,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
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

    private static async Task<T?> GetPluginDataAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(GetPluginApiUrl(path), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
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

    private static string GetPluginApiUrl(string path) => $"{ConfigManger.ApiUrl}/{PluginApiPath}/{path}";

    public static bool SupportsCurrentPlatform(IReadOnlyCollection<string> availablePlatforms)
    {
        return availablePlatforms.Count > 0 && availablePlatforms.Any(platform =>
            string.Equals(platform, GetCurrentPlatformName(), StringComparison.OrdinalIgnoreCase));
    }

    private static int GetCurrentPlatformType() => OperatingSystem.IsWindows()
        ? 1
        : OperatingSystem.IsMacOS()
            ? 2
            : 3;

    private static string GetCurrentPlatformName() => OperatingSystem.IsWindows()
        ? "windows"
        : OperatingSystem.IsMacOS()
            ? "macos"
            : "linux";
}
