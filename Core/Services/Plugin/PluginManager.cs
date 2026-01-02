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

    private static ILogger Log = LogManager.Logger.ForContext<PluginManager>();
    private static readonly ObservableCollection<PluginLocalInfo> AllPluginInfos = new();
    private static readonly Dictionary<string, Plugin> EnablePlugins = new();

    public static HttpClient _httpClient = new()
    {
        DefaultRequestHeaders =
        {
            { "User-Agent", "Kitopia/1.0.0" }
        }
    };

    public static void Init()
    {
        Kitopia.ServiceProvider = ServiceManager.Services;
        Kitopia.ISearchItemTool =
            (ISearchItemTool)ServiceManager.Services.GetService(typeof(ISearchItemTool))!;
        Kitopia.IClipboardService = ServiceManager.Services.GetService<IClipboardService>()!;
        Kitopia.IToastService = (IToastService)ServiceManager.Services.GetService(typeof(IToastService))!;
        Kitopia._i18n = CustomScenarioGloble._i18n;
        Kitopia.ToolTipConverters = CustomScenarioGloble.ToolTipConverters;
        Kitopia.JsonConverters = CustomScenarioGloble.JsonConverters;
        Kitopia.InferenceSessionManager = ServiceManager.Services.GetService<IInferenceSessionManager>()!;
        Kitopia.Logger = LogManager.Logger;
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
        RefreshPluginDependencyStatus();
        WeakReferenceMessenger.Default.Send(
            new PluginStateChanged(pluginInfoEx.PluginBaseInfo.NameSign));
    }

    public static void RefreshPluginDependencyStatus()
    {
        var allBaseInfos = AllPluginInfos.Select(x => x.PluginBaseInfo).ToList();

        foreach (var info in AllPluginInfos)
        {
            var (canLoad, versionCheckResults) = CheckDependencies(
                allBaseInfos,
                info.PluginBaseInfo.Dependencies,
                autoDownload: false,
                autoEnable: false);

            if (!canLoad)
            {
                var stringBuilder = new StringBuilder();
                foreach (var (key, value) in versionCheckResults)
                    stringBuilder.AppendLine($"{key} {value.ToString()}");

                var reason = $"依赖检查未通过:\n {stringBuilder}";
                if (!info.LoadFailed || info.LoadFailedReason != reason)
                {
                    info.LoadFailed = true;
                    info.LoadFailedReason = reason;
                    info.NotifyStatusChanged();
                }
            }
            else
            {
                if (info.LoadFailed)
                {
                    info.LoadFailed = false;
                    info.LoadFailedReason = null;
                    info.NotifyStatusChanged();
                }
            }
        }
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

    public static void DisablePlugin(PluginLocalInfo pluginInfoEx)
    {
        var deps = new HashSet<PluginLocalInfo>();
        GetAllDependentPlugins(pluginInfoEx, deps);

        if (deps.Count > 0)
        {
            var sortedDeps = new List<PluginLocalInfo>();
            try
            {
                sortedDeps = TopologicalSort(deps.ToList());
                sortedDeps.Reverse(); // Disable dependents first
            }
            catch (Exception)
            {
                sortedDeps = deps.ToList();
            }

            var content = "检测到以下插件依赖于此插件，也将被一并禁用：\n" + string.Join(", ", sortedDeps.Select(p => p.PluginBaseInfo.Name));

            var dialog = new DialogContent
            {
                Title = $"禁用 {pluginInfoEx.PluginBaseInfo.Name}?",
                Content = content,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                PrimaryAction = async () =>
                {
                    try
                    {
                        foreach (var dep in sortedDeps)
                        {
                            await UnloadPlugin(dep, false);
                        }
                        await UnloadPlugin(pluginInfoEx, true);
                    }
                    catch (Exception e)
                    {
                        Log.Error(e, "批量禁用插件时发生错误");
                    }
                }
            };
            ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null, dialog);
        }
        else
        {
            _ = UnloadPlugin(pluginInfoEx, true);
        }
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
                Thread.Sleep(50);
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
        WeakReferenceMessenger.Default.Send(new PluginsReloaded());
    }

    public static List<PluginLocalInfo> TopologicalSort(List<PluginLocalInfo> nodes)
    {
        var sorted = new List<PluginLocalInfo>();
        var visited = new HashSet<string>();
        var processing = new HashSet<string>();
        var nodeMap = nodes.ToDictionary(n => n.PluginBaseInfo.NameSign, n => n);

        void Visit(PluginLocalInfo node)
        {
            var id = node.PluginBaseInfo.NameSign;
            if (processing.Contains(id))
            {
                throw new Exception($"检测到循环依赖: {id}");
            }

            if (visited.Contains(id))
            {
                return;
            }

            processing.Add(id);

            foreach (var dep in node.PluginBaseInfo.Dependencies)
            {
                // 忽略 Kitopia 核心依赖
                if (dep.Key == "Kitopia") continue;

                if (nodeMap.TryGetValue(dep.Key, out var depNode))
                {
                    Visit(depNode);
                }
            }

            processing.Remove(id);
            visited.Add(id);
            sorted.Add(node);
        }

        foreach (var node in nodes)
        {
            Visit(node);
        }

        return sorted;
    }

    public static (bool, ConcurrentDictionary<string, VersionCheckResult>) CheckDependencies(
        List<PluginBaseInfo> previewList, Dictionary<string, string> dependencies, bool autoDownload = true,
        bool autoEnable = false)
    {
        ConcurrentDictionary<string, VersionCheckResult> results = new();
        var canLoad = true;

        // 修改为同步 foreach，避免并发问题
        foreach (var e1 in dependencies)
        {
            var (pluginSignName, verStr) = e1;
            if (pluginSignName == "Kitopia")
            {
                if (!VersionInRange(ConfigManger.Version, verStr))
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.Kitopia版本不匹配);
                    continue;
                }

                continue;
            }

            if (autoDownload)
            {
                //下载缺失依赖
                if (previewList.All(e => e.NameSign != pluginSignName))
                {
                    var onlinePluginInfo = GetOnlinePluginInfo(pluginSignName).GetAwaiter().GetResult();
                    if (onlinePluginInfo is null)
                    {
                        ServiceManager.Services.GetService<IToastService>()
                            .Show("自动下载插件失败", $"未找到ID:{pluginSignName}的插件");
                        canLoad = false;
                        results.TryAdd(pluginSignName, VersionCheckResult.依赖远端不存在);
                        continue;
                    }

                    var downloadPluginOnline = DownloadPluginAndEnable(onlinePluginInfo.Id,
                        onlinePluginInfo.NameSign,
                        targetVersion: verStr.Replace("^", "").Split("-")[0]).GetAwaiter().GetResult();

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

                var firstOrDefault2 = AllPluginInfos.FirstOrDefault(e => e.PluginBaseInfo.NameSign == pluginSignName);
                if (firstOrDefault2 is null)
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.依赖不存在);
                    continue;
                }

                var versionInRange = VersionInRange(firstOrDefault2.PluginBaseInfo.Version, verStr);
                if (!versionInRange)
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.依赖版本不匹配);
                    continue;
                }
            }

            var firstOrDefault = AllPluginInfos.FirstOrDefault(e => e.ToPlgString() == pluginSignName);
            if (firstOrDefault is null)
            {
                canLoad = false;
                results.TryAdd(pluginSignName, VersionCheckResult.依赖不存在);
                continue;
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
                    continue;
                }
            }
        }


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

        // Phase 1 & 2: 发现与补全 (Discovery & Resolution)
        List<PluginLocalInfo> candidates = new();
        bool newPluginDownloaded = true;
        int maxIterations = 5;

        while (newPluginDownloaded && maxIterations-- > 0)
        {
            newPluginDownloaded = false;
            candidates.Clear();

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
                        var readAllText = File.ReadAllText($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}manifest.json");
                        var serialize = JsonSerializer.Deserialize<PluginBaseInfo?>(readAllText);
                        if (serialize != null)
                        {
                            var pluginBaseInfo = serialize.Value;
                            candidates.Add(new PluginLocalInfo
                            {
                                PluginBaseInfo = pluginBaseInfo,
                                FullPath = $"{directoryInfo.FullName}{Path.DirectorySeparatorChar}{pluginBaseInfo.Main}",
                                Path = $"{directoryInfo.FullName}{Path.DirectorySeparatorChar}"
                            });
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, "读取插件元数据错误");
                }
            }

            var localMap = candidates.Select(c => c.PluginBaseInfo.NameSign).ToHashSet();
            
            foreach (var candidate in candidates.ToList())
            {
                foreach (var dep in candidate.PluginBaseInfo.Dependencies)
                {
                    if (dep.Key == "Kitopia") continue;

                    if (!localMap.Contains(dep.Key))
                    {
                        Log.Information($"插件 {candidate.PluginBaseInfo.Name} 缺少依赖 {dep.Key}，尝试自动下载...");
                        try 
                        {
                            var onlineInfo = GetOnlinePluginInfo(dep.Key).GetAwaiter().GetResult();
                            if (onlineInfo != null)
                            {
                                var verStr = dep.Value.Replace("^", "").Split("-")[0];
                                var success = DownloadPluginOnline(onlineInfo.Id, onlineInfo.NameSign, targetVersion: verStr).GetAwaiter().GetResult();
                                if (success)
                                {
                                    Log.Information($"依赖 {dep.Key} 下载成功，将重新扫描。");
                                    newPluginDownloaded = true;
                                    goto ReScan; 
                                }
                                else
                                {
                                    Log.Error($"依赖 {dep.Key} 下载失败。");
                                }
                            }
                            else
                            {
                                Log.Error($"依赖 {dep.Key} 在服务器上未找到。");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"尝试下载依赖 {dep.Key} 时发生异常");
                        }
                    }
                }
            }
            ReScan:;
        }

        // Phase 3: 排序 (Sorting)
        List<PluginLocalInfo> sortedCandidates;
        try
        {
            sortedCandidates = TopologicalSort(candidates);
        }
        catch (Exception e)
        {
            Log.Error(e, "插件加载排序失败，可能存在循环依赖");
            ServiceManager.Services.GetService<IToastService>()?.Show("插件加载错误", $"检测到循环依赖: {e.Message}");
            return;
        }

        // Phase 4: 多线程加载 (Multithreaded Loading)
        Log.Debug($"插件加载顺序计算完成，准备并发加载: {string.Join(" -> ", sortedCandidates.Select(c => c.PluginBaseInfo.Name))}");

        var loadingTasks = new Dictionary<string, Task>();
        var loadLock = new object();

        // 遍历排序后的列表创建任务
        foreach (var info in sortedCandidates)
        {
            // 找出当前插件的所有依赖任务
            var dependencyTasks = info.PluginBaseInfo.Dependencies
                .Where(d => d.Key != "Kitopia")
                .Select(d => loadingTasks.TryGetValue(d.Key, out var task) ? task : null)
                .Where(t => t != null)
                .ToArray();

            // 创建加载任务
            var loadTask = Task.Run(async () =>
            {
                // 1. 等待所有依赖项加载完成
                if (dependencyTasks.Length > 0)
                {
                    await Task.WhenAll(dependencyTasks!);
                }

                try
                {
                    // 2. 线程安全地注册到 AllPluginInfos
                    lock (loadLock)
                    {
                        AllPluginInfos.Add(info);
                    }

                    // 3. 依赖检查 (同步)
                    // 注意：这里 CheckDependencies 内部已改为单线程，且只读访问 AllPluginInfos (已加锁保护或认为并发读安全)
                    // 由于前面已经 await 了依赖任务，依赖项必定已在 AllPluginInfos 中
                    var (canLoad, versionCheckResults) = CheckDependencies(
                        candidates.Select(c => c.PluginBaseInfo).ToList(), 
                        info.PluginBaseInfo.Dependencies, 
                        autoDownload: false);

                    if (!canLoad)
                    {
                        var stringBuilder = new StringBuilder();
                        foreach (var (key, value) in versionCheckResults)
                            stringBuilder.AppendLine($"{key} {value.ToString()}");

                        Log.Error($"加载插件{info.PluginBaseInfo.Name}时错误, 依赖检查未通过:\n {stringBuilder}");
                        ServiceManager.Services.GetService<IToastService>()?.Show($"加载插件{info.PluginBaseInfo.Name}失败", $"依赖检查未通过:\n {stringBuilder}");
                        
                        info.LoadFailed = true;
                        info.LoadFailedReason = $"依赖检查未通过:\n {stringBuilder}";
                        info.NotifyStatusChanged();
                        return;
                    }

                    Log.Debug($"加载插件{info.PluginBaseInfo.Name}信息成功");

                    // 4. 处理更新 (文件IO，可并行)
                    if (init && File.Exists($"{info.Path}.update"))
                    {
                        var allText = await File.ReadAllTextAsync($"{info.Path}.update");
                        if (int.TryParse(allText, out var versionId))
                            await DownloadPluginOnline(info.PluginBaseInfo.Id, info.PluginBaseInfo.NameSign, versionId);

                        try
                        {
                            File.Delete($"{info.Path}.update");
                        }
                        catch (Exception e)
                        {
                            Log.Error(e, "删除更新标记文件错误");
                        }
                    }

                    // 5. 启用插件 (涉及全局状态修改，需加锁)
                    lock (loadLock)
                    {
                        if (ConfigManger.Config.EnabledPluginInfos.Any(e => e.ToPlgString() == info.PluginBaseInfo.ToPlgString()))
                        {
                            var configPluginInfo = ConfigManger.Config.EnabledPluginInfos.First(e => e.ToPlgString() == info.PluginBaseInfo.ToPlgString());
                            ConfigManger.Config.EnabledPluginInfos.RemoveAll(e => e.NameSign == configPluginInfo.NameSign);
                            ConfigManger.Config.EnabledPluginInfos.Add(info.PluginBaseInfo);

                            if (!EnablePlugins.ContainsKey(info.PluginBaseInfo.ToPlgString()))
                            {
                                // 这里调用 EnablePluginWithoutReloadOthers，其内部操作了字典，需确保在 Lock 中
                                // 注意：如果 Plugin 构造函数耗时且不涉及全局状态，可考虑移出 Lock，但为了安全起见暂且保持一致
                                EnablePluginWithoutReloadOthers(info);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e, $"加载插件 {info.PluginBaseInfo.Name} 时发生未知错误");
                    lock (loadLock)
                    {
                        if (AllPluginInfos.Contains(info)) AllPluginInfos.Remove(info);
                    }
                }
            });

            loadingTasks[info.PluginBaseInfo.NameSign] = loadTask;
        }

        // 等待所有加载任务完成
        Task.WaitAll(loadingTasks.Values.ToArray());

        Log.Debug($"加载插件信息完成共{AllPluginInfos.Count}插件被加载");
    }

    public static void DeletePlugin(string pluginSignName)
    {
        var pluginLocalInfo = AllPluginInfos.FirstOrDefault(e => e.PluginBaseInfo.NameSign == pluginSignName);
        if (pluginLocalInfo is not null) DeletePlugin(pluginLocalInfo);
    }

    private static void GetAllDependentPlugins(PluginLocalInfo target, HashSet<PluginLocalInfo> collected)
    {
        var directDeps = AllPluginInfos.Where(p => p.PluginBaseInfo.Dependencies.ContainsKey(target.PluginBaseInfo.NameSign));
        foreach (var dep in directDeps)
        {
            if (collected.Add(dep))
            {
                GetAllDependentPlugins(dep, collected);
            }
        }
    }

    public static void DeletePlugin(PluginLocalInfo pluginInfoEx)
    {
        if (pluginInfoEx is null) return;
        var deps = new HashSet<PluginLocalInfo>();
        GetAllDependentPlugins(pluginInfoEx, deps);

        var content = "是否确定删除?\n他真的会丢失很久很久(不可恢复)";
        var sortedDeps = new List<PluginLocalInfo>();
        if (deps.Count > 0)
        {
            try
            {
                sortedDeps = TopologicalSort(deps.ToList());
                sortedDeps.Reverse(); // Delete dependents first
            }
            catch (Exception)
            {
                // Fallback if topological sort fails (e.g. cycles), though cyclic deps shouldn't load
                sortedDeps = deps.ToList();
            }
            content += $"\n\n注意：以下插件依赖于此插件，也将被一并删除：\n{string.Join(", ", sortedDeps.Select(p => p.PluginBaseInfo.Name))}";
        }

        var dialog = new DialogContent
        {
            Title = $"删除{pluginInfoEx.PluginBaseInfo.Name}?",
            Content = content,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            PrimaryAction = async () =>
            {
                foreach (var dep in sortedDeps)
                {
                    await DeletePluginWithoutUserCheck(dep, false);
                }
                await DeletePluginWithoutUserCheck(pluginInfoEx, true);
            }
        };
        ((IContentDialog)ServiceManager.Services!.GetService(typeof(IContentDialog))!).ShowDialogAsync(null,
            dialog);
    }

    public static async Task DeletePluginWithoutUserCheck(PluginLocalInfo pluginInfoEx, bool reload = true)
    {
        Log.Debug($"删除插件{pluginInfoEx.PluginBaseInfo.Name}");
        await UnloadPlugin(pluginInfoEx, false);
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

        if (reload)
        {
            Reload();
            CustomScenarioManger.Reload();
        }
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
                var httpResponseMessage = await _httpClient
                    .GetAsync($"{ConfigManger.ApiUrl}/api/plugin/{pluginId}");
                var httpContent = await httpResponseMessage.Content.ReadAsStringAsync();
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