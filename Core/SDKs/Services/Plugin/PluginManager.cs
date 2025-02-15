#region

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using AvaloniaEdit.Utils;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services.Config;
using Core.ViewModel.Pages;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;
using PluginCore.Onnx;
using SixLabors.ImageSharp;
using JsonSerializer = System.Text.Json.JsonSerializer;

#endregion

namespace Core.SDKs.Services.Plugin;

public class PluginManager
{
    private static readonly ILog Log = LogManager.GetLogger(nameof(PluginManager));

    public static readonly ObservableCollection<PluginInfo> AllPluginInfos = new();
    public static readonly Dictionary<string, Plugin> EnablePlugin = new();

    public static HttpClient _httpClient = new();

    public static void Init()
    {
        PluginCore.Kitopia.ISearchItemTool =
            (ISearchItemTool)ServiceManager.Services.GetService(typeof(ISearchItemTool))!;
        PluginCore.Kitopia.IClipboardService = ServiceManager.Services.GetService<IClipboardService>();
        PluginCore.Kitopia.IToastService = (IToastService)ServiceManager.Services.GetService(typeof(IToastService))!;
        PluginCore.Kitopia._i18n = CustomScenarioGloble._i18n;
        PluginCore.Kitopia.ToolTipConverters = CustomScenarioGloble.ToolTipConverters;
        PluginCore.Kitopia.JsonConverters = CustomScenarioGloble.JsonConverters;
        PluginCore.Kitopia.InferenceSessionManager = ServiceManager.Services.GetService<IInferenceSessionManager>();
        Load(true);
    }

    private static bool VersionInRange(string version, string range)
    {
        var v = new Version(version);
        if (range.StartsWith("^"))
        {
            var r = new Version(range.Substring(1));
            return v >= r;
        }

        if (range.Contains("-"))
        {
            var strings = range.Split('-');
            var r = new Version(strings[0]);
            return v >= r && v <= new Version(strings[1]);
        }

        return version == range;
    }

    private static bool VersionInRange(Version v, string range)
    {
        if (range.StartsWith("^"))
        {
            var r = new Version(range.Substring(1));
            return v >= r;
        }

        if (range.Contains("-"))
        {
            var strings = range.Split('-');
            var r = new Version(strings[0]);
            return v >= r && v <= new Version(strings[1]);
        }

        return v == new Version(range);
    }

    /// <summary>
    /// 所有插件中搜索无论是否启用
    /// </summary>
    /// <param name="plgStr"></param>
    /// <returns></returns>
    public static PluginInfo? GetPluginByPlgStr(string plgStr)
    {
        return AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == plgStr);
    }

    public static void EnablePluginByInfo(PluginInfo pluginInfoEx)
    {
        EnablePlugin.Add(pluginInfoEx.ToPlgString(),
            new Plugin(pluginInfoEx));
        ConfigManger.Config.EnabledPluginInfos.Add(pluginInfoEx);
        ConfigManger.Save();
        pluginInfoEx.IsEnabled = true;
        // Items.ResetBindings();
        CustomScenarioManger.ReCheck(true);
    }

    public static void UnloadPlugin(PluginInfo pluginInfoEx, bool reloadPluginAndCustomScenarion = true)
    {
        Plugin.UnloadByPluginInfo(pluginInfoEx.ToPlgString(), out var weakReference);
        EnablePlugin.Remove(pluginInfoEx.ToPlgString());

        ConfigManger.Config.EnabledPluginInfos.RemoveAll(e => e.ToPlgString() == pluginInfoEx.ToPlgString());
        ConfigManger.Save();
        pluginInfoEx.IsEnabled = false;

        for (var i = 0; i < 15; i++)
        {
            GC.Collect(2, GCCollectionMode.Aggressive);
            GC.WaitForPendingFinalizers();
            Task.Delay(10).Wait();
        }

        if (weakReference.IsAlive) pluginInfoEx.UnloadFailed = true;

        // Items.ResetBindings();
        if (reloadPluginAndCustomScenarion)
        {
            Reload();
            CustomScenarioManger.Reload();
        }
    }

    public static void Reload()
    {
        for (var i = 0; i < AllPluginInfos.Count; i++) AllPluginInfos[i].Icon?.Dispose();
        AllPluginInfos.Clear();
        Load();
    }

    public enum VersionCheckResult
    {
        依赖正常,
        依赖不存在,
        依赖远端不存在,
        依赖下载失败,
        依赖未启用,
        依赖版本不匹配,
        Kitopia版本不匹配
    }

    public static (bool, ConcurrentDictionary<string, VersionCheckResult>) CheckDependencies(
        List<PluginInfo> previewList, Dictionary<string, string> dependencies, bool autoDownload = true,
        bool autoEnable = false)
    {
        ConcurrentDictionary<string, VersionCheckResult> results = new();
        var canLoad = true;

        Parallel.ForEachAsync(dependencies, async (e1, e) =>
        {
            var (pluginSignName, verStr) = e1;
            if (pluginSignName == "Kitopia")
            {
                if (!VersionInRange(ConfigManger.Version, verStr))
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.Kitopia版本不匹配);
                    return;
                }

                return;
            }

            if (autoDownload)
            {
                //下载缺失依赖
                if (previewList.All(e => e.NameSign != pluginSignName))
                {
                    var onlinePluginInfo = await GetOnlinePluginInfo(pluginSignName);
                    if (onlinePluginInfo is null)
                    {
                        ServiceManager.Services.GetService<IToastService>()
                            .Show("自动下载插件失败", $"未找到ID:{pluginSignName}的插件");
                        canLoad = false;
                        results.TryAdd(pluginSignName, VersionCheckResult.依赖远端不存在);
                        return;
                    }

                    var downloadPluginOnline = await DownloadPluginOnline(onlinePluginInfo,
                        targetVersion: verStr.Replace("^", "").Split("-")[0]);

                    if (downloadPluginOnline)
                    {
                        ServiceManager.Services.GetService<IToastService>()
                            .Show("自动下载插件成功", $"已自动下载并启用{onlinePluginInfo.Name}");
                    }
                    else
                    {
                        ServiceManager.Services.GetService<IToastService>()
                            .Show("自动下载插件失败", $"下载ID:{pluginSignName}的插件时遇到错误");
                        results.TryAdd(pluginSignName, VersionCheckResult.依赖下载失败);
                    }
                }

                var firstOrDefault2 = AllPluginInfos.FirstOrDefault(e => e.NameSign != pluginSignName);
                if (firstOrDefault2 is null)
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.依赖不存在);
                    return;
                }

                var versionInRange = VersionInRange(firstOrDefault2.Version, verStr);
                if (!versionInRange)
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.依赖版本不匹配);
                    return;
                }
            }

            var firstOrDefault = AllPluginInfos.FirstOrDefault(e => e.ToPlgString() != pluginSignName);
            if (firstOrDefault is null)
            {
                canLoad = false;
                results.TryAdd(pluginSignName, VersionCheckResult.依赖不存在);
                return;
            }

            var contains = EnablePlugin.ContainsKey(pluginSignName);
            if (!contains)
            {
                if (autoEnable)
                {
                    Load(contains);
                }
                else
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.依赖未启用);
                    return;
                }
            }
        });


        return (canLoad, results);
    }

    public static void Load(bool init = false)
    {
        var pluginsDirectoryInfo = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory + "plugins");
        if (!pluginsDirectoryInfo.Exists)
        {
            Log.Debug($"插件目录不存在创建{pluginsDirectoryInfo.FullName}");
            pluginsDirectoryInfo.Create();
        }

        List<PluginInfo> previewList = new();
        foreach (var enumerateDirectory in pluginsDirectoryInfo.EnumerateDirectories())
            if (File.Exists($"{enumerateDirectory.FullName}{Path.DirectorySeparatorChar}manifest.json"))
            {
                var readAllText =
                    File.ReadAllText($"{enumerateDirectory.FullName}{Path.DirectorySeparatorChar}manifest.json");
                var serialize = JsonSerializer.Deserialize<PluginInfo>(readAllText);
                if (serialize != null) previewList.Add(serialize);
            }

        foreach (var directoryInfo in pluginsDirectoryInfo.EnumerateDirectories())
        {
            if (File.Exists($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}.remove"))
            {
                try
                {
                    directoryInfo.Delete(true);
                }
                catch (Exception e)
                {
                    Log.Error("错误", e);
                }

                continue;
            }


            try
            {
                if (File.Exists($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}manifest.json"))
                {
                    var readAllText =
                        File.ReadAllText($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}manifest.json");
                    var serialize = JsonSerializer.Deserialize<PluginInfo>(readAllText);
                    if (serialize != null)
                    {
                        AllPluginInfos.Add(serialize);
                        serialize.FullPath = $"{directoryInfo.FullName}{Path.DirectorySeparatorChar}{serialize.Main}";
                        serialize.Path = $"{directoryInfo.FullName}{Path.DirectorySeparatorChar}";
                        serialize.IsEnabled = false;
                        var (item1, versionCheckResults) =
                            CheckDependencies(previewList, serialize.Dependencies,
                                ConfigManger.Config.EnabledPluginInfos.Any(e =>
                                    e.ToPlgString() == serialize.ToPlgString()));
                        if (!item1)
                        {
                            var stringBuilder = new StringBuilder();
                            foreach (var (key, value) in versionCheckResults)
                                stringBuilder.AppendLine($"{key} {value.ToString()}");

                            Log.Error($"加载插件{serialize.Name}时插件错误,缺失依赖\n {stringBuilder}");
                            var dialog = new DialogContent()
                            {
                                Title = $"加载插件{serialize.Name}时插件错误",
                                Content = stringBuilder,
                                CloseButtonText = "我知道了"
                            };
                            ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(
                                null,
                                dialog);
                            continue;
                        }

                        if (serialize.UpdateTargetVersion == 0) serialize.UpdateTargetVersion = serialize.VersionId;
                        if (serialize.UpdateTargetVersion != serialize.VersionId) DownloadPluginOnline(serialize).Wait();
                        Log.Debug($"加载插件{serialize.Name}信息成功");
                        if (ConfigManger.Config.EnabledPluginInfos.FirstOrDefault(e => e.ToPlgString() == serialize.ToPlgString()) is { } pluginInfo)
                        {
                            ConfigManger.Config.EnabledPluginInfos.Remove(pluginInfo);
                            ConfigManger.Config.EnabledPluginInfos.Add(serialize);
                            serialize.IsEnabled = true;
                            if (!EnablePlugin.ContainsKey(serialize.ToPlgString())){
                                Task.Run(() => { Plugin.Load(serialize); }).Wait();
                            }
                        
                        
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("错误", e);
            }
        }

        Log.Debug($"加载插件信息完成共{AllPluginInfos.Count}插件被识别");
    }

    public static void DeletePlugin(string pluginSignName)
    {
        DeletePlugin(AllPluginInfos.FirstOrDefault(e => e.NameSign == pluginSignName));
    }

    public static void DeletePlugin(PluginInfo pluginInfoEx)
    {
        var dialog = new DialogContent()
        {
            Title = $"删除{pluginInfoEx.Name}?",
            Content = "是否确定删除?\n他真的会丢失很久很久(不可恢复)",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            PrimaryAction = () => { DeletePluginWithoutCheck(pluginInfoEx); }
        };
        ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
            dialog);
    }

    public static void DeletePluginWithoutCheck(PluginInfo pluginInfoEx)
    {
        Log.Debug($"删除插件{pluginInfoEx.Name}");
        UnloadPlugin(pluginInfoEx, false);
        if (!pluginInfoEx.UnloadFailed)
        {
            var pluginsDirectoryInfo =
                new DirectoryInfo(
                    $"{AppDomain.CurrentDomain.BaseDirectory}plugins{Path.DirectorySeparatorChar}{pluginInfoEx.ToPlgString()}");
            pluginsDirectoryInfo.Delete(true);
            Task.Run(Reload);
        }
        else
        {
            File.Create(
                $"{AppDomain.CurrentDomain.BaseDirectory}plugins{Path.DirectorySeparatorChar}{pluginInfoEx.ToPlgString()}{Path.DirectorySeparatorChar}.remove");
            Task.Run(Reload);
        }

        Reload();
        CustomScenarioManger.Reload();
    }

    public static Task<OnlinePluginInfo?> GetOnlinePluginInfo(int id)
    {
        return GetOnlinePluginInfo(id.ToString());
    }

    public static async Task<OnlinePluginInfo?> GetOnlinePluginInfo(string pluginSignName)
    {
        try
        {
            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/{pluginSignName}"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("AllBeforeThisVersion", true.ToString());
            var sendAsync = await _httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var jToken = deserializeObject["data"];
            if (jToken.Type == JTokenType.Integer) return null;
            return jToken.ToObject<OnlinePluginInfo>();
        }
        catch (Exception e)
        {
            Log.Error("错误", e);
            return null;
        }
    }

    public static async Task CheckAllUpdate()
    {
        await Parallel.ForAsync(0, AllPluginInfos.Count, (i, token) =>
        {
            try
            {
                var httpResponseMessage = _httpClient
                    .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{AllPluginInfos[i].Id}").Result;
                var httpContent = httpResponseMessage.Content.ReadAsStringAsync().Result;
                var deserializeObject = (JObject)JsonConvert.DeserializeObject(httpContent);
                var o = deserializeObject["data"];
                AllPluginInfos[i].CanUpdata = o["lastVersionId"].ToObject<int>() > AllPluginInfos[i].VersionId;
                AllPluginInfos[i].CanUpdateVersion = o["lastVersion"].ToString();
                AllPluginInfos[i].CanUpdateVersionId = o["lastVersionId"].ToObject<int>();
            }
            catch (Exception e)
            {
                AllPluginInfos[i].CanUpdata = false;
            }

            return ValueTask.CompletedTask;
        });
    }

    public static async Task<bool> DownloadPluginOnline(OnlinePluginInfo plugin, int? targetVersionId = null,
        string? targetVersion = null)
    {
        object? key = targetVersionId.HasValue ? targetVersionId.Value : targetVersion ?? plugin.LastVersion;
        var downloadPlugin = await DownloadPlugin(plugin.Id, key, plugin.ToPlgString());
        if (!downloadPlugin) return false;
        var pluginInfoEx = AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == plugin.ToPlgString());
        if (pluginInfoEx is null) return false;
        EnablePluginByInfo(pluginInfoEx);
        plugin.Upadate();
        return true;
    }

    private static async Task<bool> DownloadPlugin(int id, object versionId, string plugin)
    {
        try
        {
            Log.Debug($"从服务器下载插件{plugin}(ID:{id})版本{versionId}");
            var streamAsync =
                await _httpClient.GetStreamAsync($"{ConfigManger.ApiUrl}/api/plugin/download/1/{id}/{versionId}");
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp"));
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", $"{plugin}.zip");
            using (var fs = new FileStream(path, FileMode.Create))
            {
                await streamAsync.CopyToAsync(fs);
            }

            var zipArchive = ZipFile.Open(path, ZipArchiveMode.Read);
            zipArchive.ExtractToDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin), true);
            zipArchive.Dispose();
            File.Delete(path);

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/avatar"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", id.ToString());
            var sendAsync = await _httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            using var image = Image.Load(deserializeObject["data"].ToObject<byte[]>());
            await image.SaveAsPngAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin,
                "avatar.png"));


            Reload();
        }
        catch (Exception e)
        {
            Log.Error("错误", e);
            return false;
        }

        return true;
    }

    public static async Task<bool> DownloadPluginOnline(PluginInfo plugin)
    {
        return await DownloadPlugin(plugin.Id, plugin.UpdateTargetVersion, plugin.ToPlgString());
    }
}