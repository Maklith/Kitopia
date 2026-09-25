using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Threading;
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

public static class PluginManager
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext(typeof(PluginManager));
    private static IReadOnlyList<PluginLocalInfo> AllPluginInfos = Array.Empty<PluginLocalInfo>();
    private static readonly ConcurrentDictionary<string, Plugin> EnablePlugins = new();
    private static readonly ReadOnlyDictionary<string, Plugin> EnabledView = new(EnablePlugins);
    private static readonly Dictionary<string, (WeakReference Context, bool Succeeded)> PendingUnloads = new();
    // All mutations run on the UI dispatcher. This flag rejects overlapping async operations.
    private static bool _operationInProgress;

    public static async Task InitAsync(CancellationToken cancellationToken = default)
    {
        PluginKitopia.ServiceProvider = ServiceManager.Services;
        PluginKitopia.ISearchItemTool = ServiceManager.Services.GetRequiredService<ISearchItemTool>();
        PluginKitopia.IClipboardService = ServiceManager.Services.GetRequiredService<IClipboardService>();
        PluginKitopia.IToastService = ServiceManager.Services.GetRequiredService<IToastService>();
        PluginKitopia._i18n = CustomScenarioGlobe.I18N;
        PluginKitopia.ToolTipConverters = CustomScenarioGlobe.ToolTipConverters;
        PluginKitopia.JsonConverters = CustomScenarioGlobe.JsonConverters;
        PluginKitopia.InferenceSessionManager = ServiceManager.Services.GetRequiredService<IInferenceSessionManager>();
        PluginKitopia.Logger = LogManager.Logger;
        await RunOperationAsync(async () =>
        {
            RefreshInstalled(handleRemovals: true);
            // A failed removal in an older version may have deleted the manifest before the locked DLL.
            foreach (var directory in Directory.GetDirectories(KitopiaPaths.PluginsDirectory))
            {
                if (Path.GetFileName(directory).StartsWith('.')) continue;
                var marker = Path.Combine(directory, ".update");
                if (!File.Exists(marker)) continue;
                var name = AllPluginInfos.FirstOrDefault(info =>
                    string.Equals(Path.TrimEndingDirectorySeparator(info.Path), directory, StringComparison.OrdinalIgnoreCase))
                    ?.ToPlgString() ?? Path.GetFileName(directory);
                try
                {
                    var version = (await File.ReadAllTextAsync(marker, cancellationToken)).Trim();
                    var package = await PluginNetworkService.DownloadPackageAsync(name, version, cancellationToken);
                    if (await ApplyAsync(name, package,
                            ConfigManger.Config.EnabledPluginInfos.Any(item => item.NameSign == name), cancellationToken))
                        File.Delete(marker);
                }
                catch (Exception exception) { Logger.Error(exception, "启动时更新插件 {Plugin} 失败，保留更新标记", name); }
            }
            foreach (var name in ConfigManger.Config.EnabledPluginInfos.Select(info => info.NameSign).ToArray())
            {
                if (EnablePlugins.ContainsKey(name)) continue;
                try { await ApplyAsync(name, null, true, cancellationToken); }
                catch (Exception exception)
                {
                    Logger.Error(exception, "启动插件 {Plugin} 失败", name);
                    if (GetPluginLocalInfoByPlgStr(name) is { } info)
                    {
                        info.LoadFailed = true;
                        info.LoadFailedReason = exception.Message;
                    }
                }
            }
            return true;
        }, refreshScenarios: false);
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

    public static IReadOnlyDictionary<string, Plugin> GetEnablePlugins()
    {
        return EnabledView;
    }

    public static IServiceProvider GetServiceProvider(string plgStr)
    {
        return EnablePlugins[plgStr].ServiceProvider!;
    }

    public static MethodInfo GetMethodInfo(string plgStr, string methodAbsolutelyName, string? methodId = null)
    {
        var plugin = EnablePlugins[plgStr];
        return plugin.GetMethod(methodAbsolutelyName, methodId) ??
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

    public static Task<bool> EnablePluginAsync(string pluginSign, CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => ApplyAsync(pluginSign, null, true, cancellationToken));

    private static async Task<bool> RunOperationAsync(Func<Task<bool>> operation, bool refreshScenarios = true)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => RunOperationAsync(operation, refreshScenarios));
        if (_operationInProgress)
        {
            ServiceManager.Services.GetService<IToastService>()?.Show("插件操作进行中", "请等待当前插件操作完成。");
            return false;
        }
        _operationInProgress = true;
        try { return await operation(); }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            Logger.Error(exception, "插件操作失败");
            ServiceManager.Services.GetService<IToastService>()?.Show("插件操作失败", exception.Message);
            return false;
        }
        finally
        {
            try
            {
                foreach (var info in AllPluginInfos) info.NotifyStatusChanged();
                WeakReferenceMessenger.Default.Send(new PluginsReloaded());
                if (refreshScenarios) CustomScenarioManger.ReCheck(true);
            }
            catch (Exception exception) { Logger.Error(exception, "刷新插件界面或情景失败"); }
            finally { _operationInProgress = false; }
        }
    }

    private static void RefreshInstalled(bool handleRemovals = false)
    {
        var discovered = PluginDiscoveryService.DiscoverPlugins(KitopiaPaths.PluginsDirectory, handleRemovals);
        var old = AllPluginInfos.ToDictionary(info => info.ToPlgString());
        for (var index = 0; index < discovered.Count; index++)
        {
            var info = discovered[index];
            if (old.TryGetValue(info.ToPlgString(), out var current) && current.FullPath == info.FullPath &&
                current.PluginBaseInfo.Version == info.PluginBaseInfo.Version)
                discovered[index] = current;
            discovered[index].UnloadFailed = PendingUnloads.TryGetValue(info.ToPlgString(), out var pending) &&
                                             (!pending.Succeeded || pending.Context.IsAlive);
        }
        // Publish a complete snapshot so readers cannot observe a partially rescanned list.
        AllPluginInfos = discovered.AsReadOnly();
    }

    internal static async Task EnableOneAsync(PluginLocalInfo info)
    {
        var name = info.ToPlgString();
        if (EnablePlugins.ContainsKey(name)) return;
        if (PendingUnloads.TryGetValue(name, out var previous))
        {
            if (!previous.Succeeded || previous.Context.IsAlive)
                throw new InvalidOperationException($"插件 {info.PluginBaseInfo.Name} 动态卸载失败，需要重启后再启用。");
            PendingUnloads.Remove(name);
            info.UnloadFailed = false;
        }
        ValidateDependencies(info, AllPluginInfos, EnablePlugins.Keys);
        var plugin = new Plugin(info);
        try
        {
            plugin.Load();
            EnablePlugins[name] = plugin;
            plugin.Enable();
            info.LoadFailed = false;
            info.LoadFailedReason = null;
        }
        catch (Exception exception)
        {
            EnablePlugins.TryRemove(name, out _);
            var cleanup = await plugin.UnloadAsync();
            PendingUnloads[name] = cleanup;
            info.UnloadFailed = !cleanup.Succeeded || cleanup.Context.IsAlive;
            info.LoadFailed = true;
            info.LoadFailedReason = exception.Message;
            throw;
        }
    }

    internal static void ValidateDependencies(PluginLocalInfo info, IEnumerable<PluginLocalInfo> available,
        IEnumerable<string> enabled)
    {
        var (valid, errors) = PluginDependencyService.CheckDependencies(
            available.Select(item => item.PluginBaseInfo), info.PluginBaseInfo.Dependencies, enabled);
        if (!valid) throw new InvalidOperationException($"插件 {info.PluginBaseInfo.Name} 依赖检查失败：" +
            string.Join("；", errors.Select(error => error.Key == "Kitopia"
                ? $"Kitopia：{error.Value}（当前 {ConfigManger.Version}，要求 {info.PluginBaseInfo.Dependencies[error.Key]}）"
                : $"{error.Key}：{error.Value}")));
    }

    internal static async Task<bool> UnloadCoreAsync(PluginLocalInfo info)
    {
        var name = info.ToPlgString();
        if (EnablePlugins.TryGetValue(name, out var plugin))
        {
            var cleanup = await plugin.UnloadAsync();
            EnablePlugins.TryRemove(name, out _);
            PendingUnloads[name] = cleanup;
        }
        if (PendingUnloads.TryGetValue(name, out var pending))
        {
            // Unload only requests collection. Yield so cleanup frames can unwind and finalizers can run.
            // Bound verification: a retained plugin must fail instead of silently starting another instance.
            for (var attempt = 0; attempt < 10 && pending.Context.IsAlive; attempt++)
            {
                await Task.Delay(50);
                GC.Collect();
            }
            info.UnloadFailed = !pending.Succeeded || pending.Context.IsAlive;
        }
        else
        {
            info.UnloadFailed = false;
        }
        if (!info.UnloadFailed) PendingUnloads.Remove(name);
        else Logger.Warning("插件 {Plugin} 动态卸载失败，清理成功：{CleanupSucceeded}，程序集上下文仍存活：{ContextAlive}，需要重启",
            name, pending.Succeeded, pending.Context.IsAlive);
        return !info.UnloadFailed;
    }

    private static List<PluginLocalInfo> GetAffectedPlugins(PluginLocalInfo target)
    {
        var dependents = new HashSet<PluginLocalInfo> { target };
        PluginDependencyService.GetAllDependentPlugins(target, AllPluginInfos, dependents);
        var (sorted, cyclic) = PluginDependencyService.SafeTopologicalSort(dependents.ToList());
        sorted.AddRange(cyclic);
        sorted.Reverse();
        return sorted;
    }

    private static async Task PrepareDependenciesAsync(PluginLocalInfo root, Dictionary<string, PluginLocalInfo> candidates,
        List<PluginPackage> packages, HashSet<string> visited, HashSet<string> visiting, CancellationToken cancellationToken)
    {
        var name = root.ToPlgString();
        if (visiting.Contains(name)) throw new InvalidOperationException($"插件依赖存在循环：{name}");
        if (visited.Contains(name)) return;
        visiting.Add(name);
        foreach (var (dependency, range) in root.PluginBaseInfo.Dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency == "Kitopia") continue;
            if (!candidates.TryGetValue(dependency, out var info))
            {
                var versions = await PluginNetworkService.GetVersionDetailsAsync(dependency, null, cancellationToken);
                var version = PluginDependencyService.SelectDependencyVersion(
                    versions?.Where(item => item.CanDownload &&
                        PluginNetworkService.SupportsCurrentPlatform(item.AvailablePlatforms))
                        .Select(item => item.Version) ?? [], range);
                if (version is null) throw new InvalidOperationException($"找不到依赖 {dependency} 满足 {range} 的可用版本。");
                var package = await PluginNetworkService.DownloadPackageAsync(dependency, version, cancellationToken);
                packages.Add(package);
                info = package.Info;
                candidates.Add(dependency, info);
            }
            if (!PluginDependencyService.VersionInRange(info.PluginBaseInfo.Version, range))
                throw new InvalidOperationException($"依赖 {dependency} 的版本 {info.PluginBaseInfo.Version} 不满足 {range}。");
            await PrepareDependenciesAsync(info, candidates, packages, visited, visiting, cancellationToken);
        }
        visiting.Remove(name);
        visited.Add(name);
    }

    private static async Task<bool> ApplyAsync(string name, PluginPackage? replacement, bool enable,
        CancellationToken cancellationToken)
    {
        var packages = new List<PluginPackage>();
        if (replacement is not null) packages.Add(replacement);
        var desiredBefore = ConfigManger.Config.EnabledPluginInfos.ToArray();
        var runningBefore = EnablePlugins.Keys.ToHashSet();
        var started = new List<PluginLocalInfo>();
        var stopped = new List<PluginLocalInfo>();
        var updatePending = false;
        string? updateDirectory = null;
        try
        {
            RefreshInstalled();
            if (replacement is not null)
                updateDirectory = GetPluginLocalInfoByPlgStr(name)?.Path ?? KitopiaPaths.GetPluginDirectory(name);
            var candidates = AllPluginInfos.ToDictionary(info => info.ToPlgString());
            if (replacement is not null) candidates[name] = replacement.Info;
            if (!candidates.TryGetValue(name, out var root)) throw new InvalidOperationException($"插件 {name} 未安装。");
            var closure = new HashSet<string>();
            await PrepareDependenciesAsync(root, candidates, packages, closure, new HashSet<string>(), cancellationToken);
            var toEnable = new HashSet<string>(runningBefore);
            if (enable) toEnable.UnionWith(closure);
            foreach (var target in toEnable.Concat(closure).Distinct())
                ValidateDependencies(candidates[target], candidates.Values, toEnable.Union(closure));
            var order = PluginDependencyService.TopologicalSort(candidates.Values.Where(info =>
                toEnable.Contains(info.ToPlgString())).ToList());
            cancellationToken.ThrowIfCancellationRequested();

            if (replacement is not null && GetPluginLocalInfoByPlgStr(name) is { } old)
            {
                foreach (var affected in GetAffectedPlugins(old).Where(info => runningBefore.Contains(info.ToPlgString())))
                {
                    stopped.Add(affected);
                    if (!await UnloadCoreAsync(affected))
                    {
                        updatePending = true;
                        throw new InvalidOperationException($"插件 {affected.PluginBaseInfo.Name} 仍被引用，已保留启用设置并安排重启更新。");
                    }
                }
                if (!await UnloadCoreAsync(old))
                {
                    updatePending = true;
                    throw new InvalidOperationException($"插件 {old.PluginBaseInfo.Name} 尚未完全卸载，需要重启更新。");
                }
            }
            try
            {
                foreach (var package in packages)
                    package.Activate(GetPluginLocalInfoByPlgStr(package.Info.ToPlgString())?.Path ??
                        KitopiaPaths.GetPluginDirectory(package.Info.ToPlgString()));
            }
            catch (Exception exception) when (replacement is not null && exception is IOException or UnauthorizedAccessException)
            {
                updatePending = true;
                throw new IOException($"插件 {replacement.Info.PluginBaseInfo.Name} 的目录暂时无法替换，已安排下次启动重试安装。", exception);
            }
            RefreshInstalled();
            foreach (var candidate in order)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (EnablePlugins.ContainsKey(candidate.ToPlgString())) continue;
                var installed = GetPluginLocalInfoByPlgStr(candidate.ToPlgString())!;
                await EnableOneAsync(installed);
                started.Add(installed);
            }
            var desired = desiredBefore.Select(info => info.NameSign).ToHashSet();
            if (enable) desired.UnionWith(closure);
            ConfigManger.Config.EnabledPluginInfos.Clear();
            foreach (var sign in desired)
                ConfigManger.Config.EnabledPluginInfos.Add(GetPluginLocalInfoByPlgStr(sign)?.PluginBaseInfo ??
                    desiredBefore.First(info => info.NameSign == sign));
            ConfigManger.Save("KitopiaConfig");
            foreach (var package in packages) package.Complete();
            return true;
        }
        catch
        {
            for (var index = started.Count - 1; index >= 0; index--) await UnloadCoreAsync(started[index]);
            for (var index = packages.Count - 1; index >= 0; index--)
            {
                try { packages[index].Dispose(); }
                catch (Exception exception) { Logger.Error(exception, "恢复插件目录失败，备份目录已保留"); }
            }
            packages.Clear();
            ConfigManger.Config.EnabledPluginInfos.Clear();
            ConfigManger.Config.EnabledPluginInfos.AddRange(desiredBefore);
            if (updatePending && replacement is not null)
            {
                // Persist after rollback so restoring the old directory cannot discard the update request.
                Directory.CreateDirectory(updateDirectory!);
                File.WriteAllText(Path.Combine(updateDirectory!, ".update"), replacement.Info.PluginBaseInfo.Version);
                if (enable && !ConfigManger.Config.EnabledPluginInfos.Any(info => info.NameSign == name))
                {
                    ConfigManger.Config.EnabledPluginInfos.Add(replacement.Info.PluginBaseInfo);
                    ConfigManger.Save("KitopiaConfig");
                }
            }
            RefreshInstalled();
            foreach (var original in stopped.AsEnumerable().Reverse())
            {
                try { await EnableOneAsync(GetPluginLocalInfoByPlgStr(original.ToPlgString())!); }
                catch (Exception exception) { Logger.Error(exception, "恢复插件 {Plugin} 失败，保留重启时的启用设置", original.ToPlgString()); }
            }
            throw;
        }
        finally
        {
            foreach (var package in packages) package.Dispose();
        }
    }

    public static Task<bool> DownloadPluginAndEnable(string pluginSign, string? targetVersion = null,
        CancellationToken cancellationToken = default) => RunOperationAsync(async () =>
    {
        targetVersion ??= await PluginNetworkService.GetLatestVersionAsync(pluginSign, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetVersion)) return false;
        var package = await PluginNetworkService.DownloadPackageAsync(pluginSign, targetVersion, cancellationToken);
        return await ApplyAsync(pluginSign, package, true, cancellationToken);
    });

    public static Task<bool> Update(string pluginSign, string? targetVersion = null,
        CancellationToken cancellationToken = default) => RunOperationAsync(async () =>
    {
        if (GetPluginLocalInfoByPlgStr(pluginSign) is null) return false;
        targetVersion ??= await PluginNetworkService.GetLatestVersionAsync(pluginSign, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetVersion)) return false;
        var package = await PluginNetworkService.DownloadPackageAsync(pluginSign, targetVersion, cancellationToken);
        var enable = EnablePlugins.ContainsKey(pluginSign) ||
                     ConfigManger.Config.EnabledPluginInfos.Any(info => info.NameSign == pluginSign);
        return await ApplyAsync(pluginSign, package, enable, cancellationToken);
    });

    public static void DisablePlugin(PluginLocalInfo info) => RequestRemoval(info, delete: false);
    public static void DeletePlugin(PluginLocalInfo info) => RequestRemoval(info, delete: true);
    public static void DeletePlugin(string name)
    {
        if (GetPluginLocalInfoByPlgStr(name) is { } info) DeletePlugin(info);
    }

    private static void RequestRemoval(PluginLocalInfo info, bool delete)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RequestRemoval(info, delete));
            return;
        }
        var affected = GetAffectedPlugins(info);
        if (!delete && affected.Count == 1)
        {
            _ = RemovePluginsAsync(info.ToPlgString(), false);
            return;
        }
        ServiceManager.Services.GetRequiredService<IToastService>().Show(new DialogContent
        {
            Title = $"{(delete ? "删除" : "停用")}插件 {info.PluginBaseInfo.Name}",
            Content = "将处理以下插件及其依赖者：\n" + string.Join("、", affected.Select(item => item.PluginBaseInfo.Name)),
            PrimaryButtonText = "确定", CloseButtonText = "取消",
            PrimaryAction = async () => { await RemovePluginsAsync(info.ToPlgString(), delete); }
        }.ToToastRequest());
    }

    private static Task<bool> RemovePluginsAsync(string name, bool delete) => RunOperationAsync(async () =>
    {
        if (GetPluginLocalInfoByPlgStr(name) is not { } root) return false;
        var affected = GetAffectedPlugins(root);
        foreach (var info in affected)
        {
            var unloaded = await UnloadCoreAsync(info);
            ConfigManger.Config.EnabledPluginInfos.RemoveAll(item => item.NameSign == info.ToPlgString());
            if (!unloaded)
                ServiceManager.Services.GetService<IToastService>()?.Show("插件动态卸载失败",
                    $"插件 {info.PluginBaseInfo.Name} 尚未完全释放，请重启后完成{(delete ? "删除" : "停用")}。");
            if (!delete) continue;
            File.Delete(Path.Combine(info.Path, ".update"));
            try
            {
                if (!unloaded) throw new IOException("插件仍被引用。");
                PluginDiscoveryService.RemovePluginDirectory(info.Path);
            }
            catch (IOException) { File.WriteAllText(Path.Combine(info.Path, ".remove"), string.Empty); }
            catch (UnauthorizedAccessException) { File.WriteAllText(Path.Combine(info.Path, ".remove"), string.Empty); }
        }
        ConfigManger.Save("KitopiaConfig");
        RefreshInstalled();
        return true;
    });
}
