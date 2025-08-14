#region

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using Core.CustomScenario;
using Core.SDKs.CustomScenario;
using Core.Services.Config;
using Core.Utils;
using Core.ViewModel.Pages;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PluginCore;
using PluginCore.Onnx;
using Serilog;
using JsonSerializer = System.Text.Json.JsonSerializer;

#endregion

namespace Core.Services.Plugin;

public class PluginManager
{
    private static ILogger Log = LogManager.Logger.ForContext<PluginManager>();
    private static readonly ObservableCollection<PluginLocalInfo> AllPluginInfos = new();
    private static readonly Dictionary<string, Plugin> EnablePlugins = new();

    public static HttpClient _httpClient = new();

    public static void Init()
    {
        PluginCore.Kitopia.ISearchItemTool =
            (ISearchItemTool)ServiceManager.Services.GetService(typeof(ISearchItemTool))!;
        PluginCore.Kitopia.IClipboardService = ServiceManager.Services.GetService<IClipboardService>()!;
        PluginCore.Kitopia.IToastService = (IToastService)ServiceManager.Services.GetService(typeof(IToastService))!;
        PluginCore.Kitopia._i18n = CustomScenarioGloble._i18n;
        PluginCore.Kitopia.ToolTipConverters = CustomScenarioGloble.ToolTipConverters;
        PluginCore.Kitopia.JsonConverters = CustomScenarioGloble.JsonConverters;
        PluginCore.Kitopia.InferenceSessionManager = ServiceManager.Services.GetService<IInferenceSessionManager>()!;
        PluginCore.Kitopia.Logger = LogManager.Logger;
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
    public static PluginLocalInfo? GetPluginLocalInfoByPlgStr(string plgStr)
    {
        return AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == plgStr);
    }

    public static PluginLocalInfo? GetPluginLocalInfoOnlyOnEnableByPlgStr(string plgStr)
    {
        return EnablePlugins.TryGetValue(plgStr, out var value) ? value.PluginInfo : null;
    }

    public static PluginBaseInfo? GetPluginBaseInfoByType(Type type)
    {
        var firstOrDefault = EnablePlugins.FirstOrDefault((e) => e.Value.IsPluginAssembly(type.Assembly));
        if (firstOrDefault.Value is null) return null;
        return firstOrDefault.Value.PluginInfo.PluginBaseInfo;
    }

    public static bool IsTypeFromThePlugin(Type type, string pluginName)
    {
        var firstOrDefault = EnablePlugins.FirstOrDefault((e) => e.Key == pluginName);
        if (firstOrDefault.Value is null) return false;
        return firstOrDefault.Value.IsPluginAssembly(type.Assembly);
    }

    public static IEnumerable<PluginLocalInfo> GetPluginLocalInfos()
    {
        return AllPluginInfos;
    }

    public static Dictionary<string, Plugin> GetEnablePlugins()
    {
        return EnablePlugins;
    }

    public static IServiceProvider GetServiceProvider(string plgStr)
    {
        return EnablePlugins[plgStr].ServiceProvider!;
    }

    public static MethodInfo GetMethodInfo(string plgStr, string methodAbsolutelyName)
    {
        var plugin = EnablePlugins[plgStr];
        return plugin.GetMethod(methodAbsolutelyName) ??
               throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.方法未找到, plgStr,
                   methodAbsolutelyName);
    }

    public static Type GetType(string[] strings)
    {
        if (EnablePlugins.TryGetValue(strings[0], out var value))
            return value.GetType(strings[1]) ??
                   throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.类未找到, strings[0],
                       strings[1]);

        throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未找到, strings[0],
            strings[1]);
    }

    public static void EnablePlugin(PluginLocalInfo pluginInfoEx)
    {
        if (EnablePlugins.ContainsKey(pluginInfoEx.ToPlgString())) return;
        EnablePluginWithoutReloadOthers(pluginInfoEx);
        CustomScenarioManger.ReCheck(true);
        WeakReferenceMessenger.Default.Send(
            new PluginStateChanged(pluginInfoEx.PluginBaseInfo.NameSign));
    }

    public static void EnablePluginWithoutReloadOthers(PluginLocalInfo pluginInfoEx)
    {
        if (EnablePlugins.ContainsKey(pluginInfoEx.ToPlgString())) return;

        EnablePlugins.Add(pluginInfoEx.ToPlgString(),
            new Plugin(pluginInfoEx));
        ConfigManger.Config.EnabledPluginInfos.Add(pluginInfoEx.PluginBaseInfo);
        ConfigManger.Save();
    }

    public static bool EnablePlugin(string pluginSign)
    {
        var pluginInfoEx = AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == pluginSign);
        if (pluginInfoEx is null) return false;
        EnablePlugin(pluginInfoEx);
        return true;
    }

    public static async Task<bool> UnloadPlugin(PluginLocalInfo pluginInfoEx,
        bool reloadPluginAndCustomScenarion = true)
    {
        WeakReference? weakReference = null;
        await Task.Run(() =>
        {
            Plugin.UnloadByPluginInfo(pluginInfoEx.ToPlgString(), out weakReference);
            EnablePlugins.Remove(pluginInfoEx.ToPlgString());

            ConfigManger.Config.EnabledPluginInfos.RemoveAll(e => e.ToPlgString() == pluginInfoEx.ToPlgString());
            ConfigManger.Save();

            for (var i = 0; i < 30; i++)
            {
                GC.Collect(2, GCCollectionMode.Aggressive);
                GC.WaitForPendingFinalizers();
                Task.Delay(50).Wait();
                if (!weakReference.IsAlive) break;
            }
        });
        if (weakReference is null) return false;

        if (weakReference.IsAlive)
        {
            pluginInfoEx.UnloadFailed = true;
            Task.Run(() =>
            {
                while (weakReference.IsAlive) Thread.Sleep(1000);

                pluginInfoEx.UnloadFailed = false;
            });
        }

        // Items.ResetBindings();
        if (reloadPluginAndCustomScenarion)
        {
            WeakReferenceMessenger.Default.Send(
                new PluginStateChanged(pluginInfoEx.PluginBaseInfo.NameSign));
            Reload();
            CustomScenarioManger.Reload();
        }

        return false;
    }

    public static void Reload()
    {
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
        List<PluginBaseInfo> previewList, Dictionary<string, string> dependencies, bool autoDownload = true,
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

                    var downloadPluginOnline = await DownloadPluginAndEnable(onlinePluginInfo.Id,
                        onlinePluginInfo.NameSign,
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

                var firstOrDefault2 = AllPluginInfos.FirstOrDefault(e => e.PluginBaseInfo.NameSign != pluginSignName);
                if (firstOrDefault2 is null)
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.依赖不存在);
                    return;
                }

                var versionInRange = VersionInRange(firstOrDefault2.PluginBaseInfo.Version, verStr);
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

            var contains = EnablePlugins.ContainsKey(pluginSignName);
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

        List<PluginBaseInfo> previewList = new();
        foreach (var enumerateDirectory in pluginsDirectoryInfo.EnumerateDirectories())
            if (File.Exists($"{enumerateDirectory.FullName}{Path.DirectorySeparatorChar}manifest.json"))
            {
                var readAllText =
                    File.ReadAllText($"{enumerateDirectory.FullName}{Path.DirectorySeparatorChar}manifest.json");
                var serialize = JsonSerializer.Deserialize<PluginBaseInfo?>(readAllText);
                if (serialize != null) previewList.Add(serialize.Value);
            }

        foreach (var directoryInfo in pluginsDirectoryInfo.EnumerateDirectories())
        {
            if (init && File.Exists($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}.remove"))
            {
                try
                {
                    directoryInfo.Delete(true);
                }
                catch (Exception e)
                {
                    Log.Error(e, "错误");
                }

                continue;
            }


            try
            {
                if (File.Exists($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}manifest.json"))
                {
                    var readAllText =
                        File.ReadAllText($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}manifest.json");
                    var serialize = JsonSerializer.Deserialize<PluginBaseInfo?>(readAllText);
                    if (serialize != null)
                    {
                        var pluginBaseInfo = serialize.Value;

                        var info = new PluginLocalInfo
                        {
                            PluginBaseInfo = pluginBaseInfo,
                            FullPath = $"{directoryInfo.FullName}{Path.DirectorySeparatorChar}{pluginBaseInfo.Main}",
                            Path = $"{directoryInfo.FullName}{Path.DirectorySeparatorChar}"
                        };
                        AllPluginInfos.Add(info);

                        var (item1, versionCheckResults) =
                            CheckDependencies(previewList, pluginBaseInfo.Dependencies,
                                ConfigManger.Config.EnabledPluginInfos.Any(e =>
                                    e.ToPlgString() == pluginBaseInfo.ToPlgString()));
                        if (!item1)
                        {
                            var stringBuilder = new StringBuilder();
                            foreach (var (key, value) in versionCheckResults)
                                stringBuilder.AppendLine($"{key} {value.ToString()}");

                            Log.Error($"加载插件{pluginBaseInfo.Name}时插件错误,缺失依赖\n {stringBuilder}");
                            var dialog = new DialogContent
                            {
                                Title = $"加载插件{pluginBaseInfo.Name}时插件错误",
                                Content = stringBuilder,
                                CloseButtonText = "我知道了"
                            };
                            ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!)
                                .ShowDialogAsync(
                                    null,
                                    dialog);
                            continue;
                        }

                        Log.Debug($"加载插件{pluginBaseInfo.Name}信息成功");
                        if (init && File.Exists($"{info.Path}.update"))
                        {
                            var allText = File.ReadAllText($"{info.Path}.update");
                            if (int.TryParse(allText, out var versionId))
                                DownloadPluginOnline(pluginBaseInfo.Id, pluginBaseInfo.NameSign, versionId).Wait();

                            try
                            {
                                File.Delete($"{info.Path}.update");
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, "错误");
                            }
                        }

                        if (ConfigManger.Config.EnabledPluginInfos.Any(e =>
                                e.ToPlgString() == pluginBaseInfo.ToPlgString()))
                        {
                            var pluginInfo =
                                ConfigManger.Config.EnabledPluginInfos.First(e =>
                                    e.ToPlgString() == pluginBaseInfo.ToPlgString());
                            ConfigManger.Config.EnabledPluginInfos.RemoveAll(e => e.NameSign == pluginInfo.NameSign);
                            ConfigManger.Config.EnabledPluginInfos.Add(pluginBaseInfo);

                            if (!EnablePlugins.ContainsKey(pluginBaseInfo.ToPlgString()))
                            {
                                if (init)
                                    Task.Run(() => { EnablePluginWithoutReloadOthers(info); }).Wait();
                                else
                                    Task.Run(() => { EnablePlugin(info); }).Wait();
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "错误");
            }
        }

        Log.Debug($"加载插件信息完成共{AllPluginInfos.Count}插件被识别");
    }

    public static void DeletePlugin(string pluginSignName)
    {
        DeletePlugin(AllPluginInfos.FirstOrDefault(e => e.PluginBaseInfo.NameSign == pluginSignName));
    }

    public static void DeletePlugin(PluginLocalInfo pluginInfoEx)
    {
        var dialog = new DialogContent
        {
            Title = $"删除{pluginInfoEx.PluginBaseInfo.Name}?",
            Content = "是否确定删除?\n他真的会丢失很久很久(不可恢复)",
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            PrimaryAction = () => { DeletePluginWithoutUserCheck(pluginInfoEx); }
        };
        ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
            dialog);
    }

    public static void DeletePluginWithoutUserCheck(PluginLocalInfo pluginInfoEx)
    {
        Log.Debug($"删除插件{pluginInfoEx.PluginBaseInfo.Name}");
        UnloadPlugin(pluginInfoEx, false);
        if (!pluginInfoEx.UnloadFailed)
        {
            var pluginsDirectoryInfo =
                new DirectoryInfo(pluginInfoEx.Path);
            pluginsDirectoryInfo.Delete(true);
            //Task.Run(Reload);
        }
        else
        {
            File.Create(
                $"{pluginInfoEx.Path}.remove");
            //Task.Run(Reload);
        }

        Reload();
        CustomScenarioManger.Reload();
    }

    public static Task<OnlinePluginInfo?> GetOnlinePluginInfo(int id, bool allBeforeThisVersion = false)
    {
        return GetOnlinePluginInfo(id.ToString(), allBeforeThisVersion);
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
            var sendAsync = await _httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var jToken = deserializeObject["data"];
            if (jToken.Type == JTokenType.Integer) return null;
            return jToken.ToObject<OnlinePluginInfo>();
        }
        catch (Exception e)
        {
            Log.Error(e, "错误");
            return null;
        }
    }

    public static async Task<bool> DownloadPluginAndEnable(int pluginId, string pluginSign, int? targetVersionId = null,
        string? targetVersion = null)
    {
        var downloadPluginOnline = await DownloadPluginOnline(pluginId, pluginSign, targetVersionId, targetVersion);
        if (downloadPluginOnline) return EnablePlugin(pluginSign);
        return false;
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

            var request = new HttpRequestMessage
            {
                RequestUri = new Uri($"{ConfigManger.ApiUrl}/api/plugin/avatar"),
                Method = HttpMethod.Get
            };
            request.Headers.Add("id", id.ToString());
            var sendAsync = await _httpClient.SendAsync(request);
            var stringAsync = await sendAsync.Content.ReadAsStringAsync();
            var deserializeObject = (JObject)JsonConvert.DeserializeObject(stringAsync);
            var arr = deserializeObject["data"].ToObject<byte[]>(); //将指定的字符串（它将二进制数据编码为 Base64 数字）转换为等效的 8 位无符号整数数组。
            using (var ms = new MemoryStream(arr))
            {
                var filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin,
                    "avatar.png");
                var directoryname = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", plugin);
                var bmp = new Bitmap(ms, true); //加载图像

                if (!Directory.Exists(directoryname)) //判断保存目录是否存在
                    Directory.CreateDirectory(directoryname);
                bmp.Save(filename, ImageFormat.Png); //将图片以JPEG格式保存在指定目录(可以选择其他图片格式)
                ms.Close(); //关闭流并释放
            }


            Reload();
        }
        catch (Exception e)
        {
            Log.Error(e, "错误");
            return false;
        }

        return true;
    }

    public static async Task<bool> DownloadPluginOnline(int pluginId, string pluginSign, int? targetVersionId = null,
        string? targetVersion = null)
    {
        object? key = targetVersionId.HasValue ? targetVersionId.Value : targetVersion;
        if (key is null) return false;
        var downloadPlugin = await DownloadPlugin(pluginId, key, pluginSign);
        if (!downloadPlugin) return false;
        var pluginInfoEx = AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == pluginSign);
        if (pluginInfoEx is null) return false;
        return true;
    }

    public static async Task<bool> Update(int pluginId, string pluginSign, int? targetVersionId = null)
    {
        try
        {
            if (targetVersionId is null)
            {
                var httpResponseMessage = _httpClient
                    .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{pluginId}").Result;
                var httpContent = httpResponseMessage.Content.ReadAsStringAsync().Result;
                var deserializeObject = (JObject)JsonConvert.DeserializeObject(httpContent);
                targetVersionId = deserializeObject["data"]["lastVersionId"].ToObject<int>();
            }


            var pluginLocalInfoByPlgStr = GetPluginLocalInfoByPlgStr(pluginSign);
            if (pluginLocalInfoByPlgStr is null) return false;
            await UnloadPlugin(pluginLocalInfoByPlgStr);

            if (pluginLocalInfoByPlgStr.UnloadFailed)
            {
                await File.WriteAllTextAsync($"{pluginLocalInfoByPlgStr.Path}.update", targetVersionId.ToString());
            }
            else
            {
                var downloadPluginAndEnable = await DownloadPluginAndEnable(pluginLocalInfoByPlgStr.PluginBaseInfo.Id,
                    pluginLocalInfoByPlgStr.PluginBaseInfo.NameSign, targetVersionId);
                if (!downloadPluginAndEnable)
                    ServiceManager.Services.GetService<IToastService>()!.Show("更新插件失败",
                        $"更新插件{pluginLocalInfoByPlgStr.PluginBaseInfo.Name}失败");
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "错误");
            return false;
        }

        return false;
    }
}