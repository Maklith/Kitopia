using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using Core.Services.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace Core.Services.Plugin;

public class PluginNetworkService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<PluginNetworkService>();

    public static readonly HttpClient HttpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", $"Kitopia/{ConfigManger.Version}" }
        }
    };

    public static async Task<OnlinePluginInfo?> GetOnlinePluginInfo(int id, bool allBeforeThisVersion = false)
    {
        return await GetOnlinePluginInfo(id.ToString(), allBeforeThisVersion);
    }

    public static async Task<OnlinePluginInfo?> GetOnlinePluginInfo(string pluginSignName,
        bool allBeforeThisVersion = false)
    {
        try
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/{pluginSignName}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("AllBeforeThisVersion", allBeforeThisVersion.ToString());
            var sendAsync = await HttpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var jToken = deserializeObject["data"];
            if (jToken.Type == JTokenType.Integer) return null;
            return jToken.ToObject<OnlinePluginInfo>();
        }
        catch (Exception e)
        {
            Logger.Error(e, "获取插件信息错误");
            return null;
        }
    }

    public static async Task<bool> DownloadPlugin(int id, object versionId, string plugin)
    {
        try
        {
            Logger.Debug($"从服务器下载插件{plugin}(ID:{id})版本{versionId}");
            var streamAsync =
                await HttpClient.GetStreamAsync($"{ConfigManger.ApiUrl}/api/plugin/download/1/{id}/{versionId}");
            
            var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
            Directory.CreateDirectory(tempDir);
            var path = Path.Combine(tempDir, $"{plugin}.zip");
            
            using (var fs = new FileStream(path, FileMode.Create))
            {
                await streamAsync.CopyToAsync(fs);
            }

            var pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin);
            // Ensure clean install? PluginManager didn't seem to clear it first in DownloadPlugin, 
            // but Load(init=true) handles .remove.
            // Here we just extract.
            
            var zipArchive = ZipFile.Open(path, ZipArchiveMode.Read);
            zipArchive.ExtractToDirectory(pluginDir, true);
            zipArchive.Dispose();
            File.Delete(path);

            await DownloadAvatar(id, plugin);
        }
        catch (Exception e)
        {
            Logger.Error(e, "下载插件错误");
            return false;
        }

        return true;
    }

    public static async Task<byte[]?> GetAvatarBytesAsync(int pluginId, CancellationToken cts = default)
    {
        try
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/avatar"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", pluginId.ToString());
            var sendAsync = await HttpClient.SendAsync(request, cts);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync(cts);
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            if (deserializeObject["flag"].ToObject<bool>())
            {
                return deserializeObject["data"].ToObject<byte[]>();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "获取插件图标错误");
        }
        return null;
    }

    private static async Task DownloadAvatar(int id, string plugin)
    {
        try
        {
            var arr = await GetAvatarBytesAsync(id);
            if (arr == null) return;

            using (var ms = new MemoryStream(arr))
            {
                var filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin, "avatar.png");
                var directoryname = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin);
                
                if (!Directory.Exists(directoryname))
                    Directory.CreateDirectory(directoryname);
                
                // Note: Windows specific System.Drawing.Common. 
                // Ensure platform compatibility if Linux is supported, but current OS is win32.
                // Assuming System.Drawing is available (nuget package).
                var bmp = new Bitmap(ms, true);
                bmp.Save(filename, ImageFormat.Png);
                ms.Close();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "下载插件图标错误");
        }
    }

    public static async Task<string?> GetAuthorNameAsync(int authorId, CancellationToken cts = default)
    {
        try
        {
            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/user/baseInfo"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", authorId.ToString());
            var async = await HttpClient.SendAsync(request, cts);
            var stringAsync = await async.Content.ReadAsStringAsync(cts);
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);

            return deserializeObject["data"]["userName"].ToString();
        }
        catch (Exception e)
        {
            Logger.Error(e, "获取作者信息错误");
            return null;
        }
    }

    public static async Task<(int VersionId, string Version)?> GetLatestVersionInfoAsync(int pluginId, CancellationToken cts = default)
    {
        try
        {
            var httpResponseMessage = await HttpClient
                .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{pluginId}", cts);
            var httpContent = await httpResponseMessage.Content.ReadAsStringAsync(cts);
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(httpContent);
            var o = deserializeObject["data"];
            if (o.Type == JTokenType.Integer)
            {
                return null;
            }

            return (o["lastVersionId"].ToObject<int>(), o["lastVersion"].ToString());
        }
        catch (Exception e)
        {
            Logger.Error(e, "获取最新版本信息错误");
            return null;
        }
    }

    public static async Task<List<VersionDetail>?> GetVersionDetailsAsync(int pluginId, int? lastVersionId = null, CancellationToken cts = default)
    {
        try
        {
            if (lastVersionId == null)
            {
                var latest = await GetLatestVersionInfoAsync(pluginId, cts);
                if (latest == null) return null;
                lastVersionId = latest.Value.VersionId;
            }

            var request = new HttpRequestMessage
            {
                RequestUri =
                    new Uri(
                        $"{ConfigManger.ApiUrl}/api/plugin/detail/{pluginId}/{lastVersionId}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("AllBeforeThisVersion", true.ToString());
            var sendAsync = await HttpClient.SendAsync(request, cts);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync(cts);
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var list = deserializeObject["data"].ToObject<List<VersionDetail>>();
            list.Reverse();
            return list;
        }
        catch (Exception e)
        {
            Logger.Error(e, "获取版本详情错误");
            return null;
        }
    }
}