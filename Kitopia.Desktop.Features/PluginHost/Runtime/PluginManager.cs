using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;
using PluginCore.Onnx;
using Serilog;
using PluginKitopia = PluginCore.Kitopia;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginManager
{
    private static ILogger Logger = LogManager.Logger.ForContext<PluginManager>();
    private static readonly ObservableCollection<PluginLocalInfo> AllPluginInfos = new();
    private static readonly Dictionary<string, Plugin> EnablePlugins = new();

    public static void Init()
    {
        PluginKitopia.ServiceProvider = ServiceManager.Services;
        PluginKitopia.ISearchItemTool =
            (ISearchItemTool)ServiceManager.Services.GetService(typeof(ISearchItemTool))!;
        PluginKitopia.IClipboardService = ServiceManager.Services.GetService<IClipboardService>()!;
        PluginKitopia.IToastService = (IToastService)ServiceManager.Services.GetService(typeof(IToastService))!;
        PluginKitopia._i18n = CustomScenarioGlobe.I18N;
        PluginKitopia.ToolTipConverters = CustomScenarioGlobe.ToolTipConverters;
        PluginKitopia.JsonConverters = CustomScenarioGlobe.JsonConverters;
        PluginKitopia.InferenceSessionManager = ServiceManager.Services.GetService<IInferenceSessionManager>()!;
        PluginKitopia.Logger = LogManager.Logger;
        Load(true);
    }

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
        var enabledSignatures = EnablePlugins.Keys.ToList();

        foreach (var info in AllPluginInfos)
        {
            var (canLoad, versionCheckResults) = PluginDependencyService.CheckDependencies(
                allBaseInfos,
                info.PluginBaseInfo.Dependencies,
                enabledSignatures);

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
        PluginDependencyService.GetAllDependentPlugins(pluginInfoEx, AllPluginInfos, deps);

        if (deps.Count > 0)
        {
            var sortedDeps = new List<PluginLocalInfo>();
            try
            {
                sortedDeps = PluginDependencyService.TopologicalSort(deps.ToList());
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
                        Logger.Error(e, "批量禁用插件时发生错误");
                    }
                }
            };
            ((IToastService)ServiceManager.Services!.GetService(typeof(IToastService))!).Show(
                dialog.ToToastRequest());
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

    public static void Load(bool init = false)
    {
        var pluginsPath = KitopiaPaths.PluginsDirectory;
        
        // Phase 1: Discovery
        var candidates = PluginDiscoveryService.DiscoverPlugins(pluginsPath, handleRemovals: init);

        // Phase 2: Resolve & Download Dependencies
        bool newPluginDownloaded = true;
        int maxIterations = 5;

        while (newPluginDownloaded && maxIterations-- > 0)
        {
            newPluginDownloaded = false;
            if (candidates.Count == 0 || maxIterations < 4)
            {
                 candidates = PluginDiscoveryService.DiscoverPlugins(pluginsPath, handleRemovals: false);
            }

            var localMap = candidates.Select(c => c.PluginBaseInfo.NameSign).ToHashSet();
            
            foreach (var candidate in candidates.ToList())
            {
                foreach (var dep in candidate.PluginBaseInfo.Dependencies)
                {
                    if (dep.Key == "Kitopia") continue;

                    if (!localMap.Contains(dep.Key))
                    {
                        Logger.Information($"插件 {candidate.PluginBaseInfo.Name} 缺少依赖 {dep.Key}，尝试自动下载...");
                        try 
                        {
                            var onlineInfo = PluginNetworkService.GetOnlinePluginInfo(dep.Key).GetAwaiter().GetResult();
                            if (onlineInfo != null)
                            {
                                var verStr = dep.Value.Replace("^", "").Split("-")[0];
                                var success = PluginNetworkService.DownloadPlugin(onlineInfo.Id, verStr, onlineInfo.NameSign).GetAwaiter().GetResult();
                                if (success)
                                {
                                    Logger.Information($"依赖 {dep.Key} 下载成功，将重新扫描。");
                                    newPluginDownloaded = true;
                                    goto ReScan; 
                                }
                                else
                                {
                                    Logger.Error($"依赖 {dep.Key} 下载失败。");
                                }
                            }
                            else
                            {
                                Logger.Error($"依赖 {dep.Key} 在服务器上未找到。");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"尝试下载依赖 {dep.Key} 时发生异常");
                        }
                    }
                }
            }
            ReScan:;
        }

        // Phase 3: Sorting
        var (sortedCandidates, cyclic) = PluginDependencyService.SafeTopologicalSort(candidates);

        var loadLock = new object();

        if (cyclic.Count > 0)
        {
            foreach (var info in cyclic)
            {
                info.LoadFailed = true;
                info.LoadFailedReason = "检测到循环依赖";
                lock (loadLock)
                {
                    AllPluginInfos.Add(info);
                }
                info.NotifyStatusChanged();
            }

            var msg = string.Join(", ", cyclic.Select(c => c.PluginBaseInfo.Name));
            Logger.Error($"插件加载检测到循环依赖，以下插件将被排除: {msg}");
            ServiceManager.Services.GetService<IToastService>()?.Show("循环依赖警告", $"以下插件因循环依赖无法加载: {msg}");
        }

        // Phase 4: Multithreaded Loading
        Logger.Debug($"插件加载顺序计算完成，准备并发加载: {string.Join(" -> ", sortedCandidates.Select(c => c.PluginBaseInfo.Name))}");

        var loadingTasks = new Dictionary<string, Task>();

        foreach (var info in sortedCandidates)
        {
            var dependencyTasks = info.PluginBaseInfo.Dependencies
                .Where(d => d.Key != "Kitopia")
                .Select(d => loadingTasks.TryGetValue(d.Key, out var task) ? task : null)
                .Where(t => t != null)
                .ToArray();

            var loadTask = Task.Run(async () =>
            {
                if (dependencyTasks.Length > 0)
                {
                    await Task.WhenAll(dependencyTasks!);
                }

                try
                {
                    lock (loadLock)
                    {
                        AllPluginInfos.Add(info);
                    }

                    // Check dependencies (ensure they are loaded and enabled)
                    var (canLoad, versionCheckResults) = PluginDependencyService.CheckDependencies(
                        candidates.Select(c => c.PluginBaseInfo), 
                        info.PluginBaseInfo.Dependencies, 
                        EnablePlugins.Keys);

                    if (!canLoad)
                    {
                        var stringBuilder = new StringBuilder();
                        foreach (var (key, value) in versionCheckResults)
                            stringBuilder.AppendLine($"{key} {value.ToString()}");

                        Logger.Error($"加载插件{info.PluginBaseInfo.Name}时错误, 依赖检查未通过:\n {stringBuilder}");
                        ServiceManager.Services.GetService<IToastService>()?.Show($"加载插件{info.PluginBaseInfo.Name}失败", $"依赖检查未通过:\n {stringBuilder}");
                        
                        info.LoadFailed = true;
                        info.LoadFailedReason = $"依赖检查未通过:\n {stringBuilder}";
                        info.NotifyStatusChanged();
                        return;
                    }

                    Logger.Debug($"加载插件{info.PluginBaseInfo.Name}信息成功");

                    if (init && File.Exists($"{info.Path}.update"))
                    {
                        var allText = await File.ReadAllTextAsync($"{info.Path}.update");
                        if (int.TryParse(allText, out var versionId))
                            await PluginNetworkService.DownloadPlugin(info.PluginBaseInfo.Id, versionId, info.PluginBaseInfo.NameSign);

                        try
                        {
                            File.Delete($"{info.Path}.update");
                        }
                        catch (Exception e)
                        {
                            Logger.Error(e, "删除更新标记文件错误");
                        }
                    }

                    lock (loadLock)
                    {
                        if (ConfigManger.Config.EnabledPluginInfos.Any(e => e.ToPlgString() == info.PluginBaseInfo.ToPlgString()))
                        {
                            var configPluginInfo = ConfigManger.Config.EnabledPluginInfos.First(e => e.ToPlgString() == info.PluginBaseInfo.ToPlgString());
                            ConfigManger.Config.EnabledPluginInfos.RemoveAll(e => e.NameSign == configPluginInfo.NameSign);
                            ConfigManger.Config.EnabledPluginInfos.Add(info.PluginBaseInfo);

                            if (!EnablePlugins.ContainsKey(info.PluginBaseInfo.ToPlgString()))
                            {
                                EnablePluginWithoutReloadOthers(info);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"加载插件 {info.PluginBaseInfo.Name} 时发生未知错误");
                    lock (loadLock)
                    {
                        if (AllPluginInfos.Contains(info)) AllPluginInfos.Remove(info);
                    }
                }
            });

            loadingTasks[info.PluginBaseInfo.NameSign] = loadTask;
        }

        Task.WaitAll(loadingTasks.Values.ToArray());

        Logger.Debug($"加载插件信息完成共{AllPluginInfos.Count}插件被加载");
    }

    public static void DeletePlugin(string pluginSignName)
    {
        var pluginLocalInfo = AllPluginInfos.FirstOrDefault(e => e.PluginBaseInfo.NameSign == pluginSignName);
        if (pluginLocalInfo is not null) DeletePlugin(pluginLocalInfo);
    }

    public static void DeletePlugin(PluginLocalInfo pluginInfoEx)
    {
        if (pluginInfoEx is null) return;
        var deps = new HashSet<PluginLocalInfo>();
        PluginDependencyService.GetAllDependentPlugins(pluginInfoEx, AllPluginInfos, deps);

        var content = "是否确定删除?\n他真的会丢失很久很久(不可恢复)";
        var sortedDeps = new List<PluginLocalInfo>();
        if (deps.Count > 0)
        {
            try
            {
                sortedDeps = PluginDependencyService.TopologicalSort(deps.ToList());
                sortedDeps.Reverse(); // Delete dependents first
            }
            catch (Exception)
            {
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
        ((IToastService)ServiceManager.Services!.GetService(typeof(IToastService))!).Show(
            dialog.ToToastRequest());
    }

    public static async Task DeletePluginWithoutUserCheck(PluginLocalInfo pluginInfoEx, bool reload = true)
    {
        Logger.Debug($"删除插件{pluginInfoEx.PluginBaseInfo.Name}");
        await UnloadPlugin(pluginInfoEx, false);
        if (!pluginInfoEx.UnloadFailed)
        {
            var pluginsDirectoryInfo =
                new DirectoryInfo(pluginInfoEx.Path);
            if (pluginsDirectoryInfo.Exists)
            {
                Logger.Information($"正在删除插件目录: {pluginsDirectoryInfo.FullName}");
                try
                {
                    pluginsDirectoryInfo.Delete(true);
                }
                catch (Exception e)
                {
                    Logger.Error(e, $"删除插件目录失败: {pluginsDirectoryInfo.FullName}");
                }
            }
            else
            {
                Logger.Warning($"插件目录不存在，跳过删除: {pluginsDirectoryInfo.FullName}");
            }
        }
        else
        {
            Logger.Warning($"插件卸载失败，创建 .remove 标记: {pluginInfoEx.Path}");
            File.Create(
                $"{pluginInfoEx.Path}.remove");
        }

        if (reload)
        {
            Reload();
            CustomScenarioManger.Reload();
        }
    }

    public static async Task<bool> DownloadPluginAndEnable(int pluginId, string pluginSign, int? targetVersionId = null,
        string? targetVersion = null)
    {
        object? key = targetVersionId.HasValue ? targetVersionId.Value : targetVersion;
        if (key is null) return false;
        
        var downloadSuccess = await PluginNetworkService.DownloadPlugin(pluginId, key, pluginSign);
        
        if (downloadSuccess) 
        {
            Reload();
            return EnablePlugin(pluginSign);
        }
        return false;
    }

    public static async Task<bool> Update(int pluginId, string pluginSign, int? targetVersionId = null)
    {
        try
        {
            if (targetVersionId is null)
            {
                var onlineInfo = await PluginNetworkService.GetOnlinePluginInfo(pluginSign);
                if (onlineInfo != null) targetVersionId = onlineInfo.LastVersionId;
            }

            if (targetVersionId is null) return false;

            var pluginLocalInfoByPlgStr = GetPluginLocalInfoByPlgStr(pluginSign);
            if (pluginLocalInfoByPlgStr is null) return false;
            await UnloadPlugin(pluginLocalInfoByPlgStr);

            if (pluginLocalInfoByPlgStr.UnloadFailed)
            {
                await File.WriteAllTextAsync($"{pluginLocalInfoByPlgStr.Path}.update", targetVersionId.ToString());
            }
            else
            {
                var downloadSuccess = await PluginNetworkService.DownloadPlugin(pluginLocalInfoByPlgStr.PluginBaseInfo.Id,
                    targetVersionId.Value,
                    pluginLocalInfoByPlgStr.PluginBaseInfo.NameSign);
                    
                if (!downloadSuccess)
                    ServiceManager.Services.GetService<IToastService>()!.Show("更新插件失败",
                        $"更新插件{pluginLocalInfoByPlgStr.PluginBaseInfo.Name}失败");
            }

            return await DownloadPluginAndEnable(pluginLocalInfoByPlgStr.PluginBaseInfo.Id, 
                pluginLocalInfoByPlgStr.PluginBaseInfo.NameSign, targetVersionId);
        }
        catch (Exception e)
        {
            Logger.Error(e, "错误");
            return false;
        }
    }
}
