using Kitopia.Feature.Localization;
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
    private static readonly ConcurrentDictionary<string, PluginDownloadProgress> ActiveDownloads = new(StringComparer.OrdinalIgnoreCase);
    public static IReadOnlyDictionary<string, PluginDownloadProgress> Downloads => ActiveDownloads;
    private static readonly Dictionary<string, (WeakReference Context, bool Succeeded)> PendingUnloads = new();
    // All mutations run on the UI dispatcher. This flag rejects overlapping async operations.
    private static bool _operationInProgress;

    public static async Task InitAsync(CancellationToken cancellationToken = default)
    {
        PluginKitopia.ServiceProvider = ServiceManager.Services;
        PluginKitopia.ISearchItemTool = ServiceManager.Services.GetRequiredService<ISearchItemTool>();
        PluginKitopia.IClipboardService = ServiceManager.Services.GetRequiredService<IClipboardService>();
        PluginKitopia.IToastService = ServiceManager.Services.GetRequiredService<IToastService>();
        PluginKitopia.TypeNames = CustomScenarioGlobe.TypeNames;
        PluginKitopia._i18n = CustomScenarioGlobe.TypeNames;
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
                    var package = await DownloadPackageAsync(name, version, cancellationToken);
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
            ServiceManager.Services.GetService<IToastService>()?.Show(Lang.Get("lang.kitopia.plugin_operation_in_progress"), Lang.Get("lang.kitopia.wait_for_the_current_plugin_operation_to_finish"));
            return false;
        }
        _operationInProgress = true;
        try { return await operation(); }
        catch (OperationCanceledException) { return false; }
        catch (Exception exception)
        {
            Logger.Error(exception, "插件操作失败");
            ServiceManager.Services.GetService<IToastService>()?.Show(Lang.Get("lang.kitopia.plugin_operation_failed"), exception.Message);
            return false;
        }
        finally
        {
            try
            {
                foreach (var progress in ActiveDownloads.Values)
                    ReportDownloadProgress(progress with { IsDownloading = false });
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
                throw new InvalidOperationException(Lang.Format("lang.kitopia.messages.plugin_value_could_not_be_unloaded_restart_before_enabling_it", info.PluginBaseInfo.Name));
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

    private static async Task<bool> PrepareDependenciesAsync(PluginLocalInfo root,
        Dictionary<string, PluginLocalInfo> candidates, List<PluginPackage> packages,
        Dictionary<string, Dictionary<string, string>> requirements, HashSet<string> visited,
        HashSet<string> visiting, CancellationToken cancellationToken)
    {
        var name = root.ToPlgString();
        if (visiting.Contains(name)) throw new InvalidOperationException($"插件依赖存在循环：{name}");
        if (visited.Contains(name)) return false;
        visiting.Add(name);
        foreach (var (dependency, range) in root.PluginBaseInfo.Dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (dependency == "Kitopia") continue;
            if (visiting.Contains(dependency)) throw new InvalidOperationException($"插件依赖存在循环：{dependency}");
            if (!requirements.TryGetValue(dependency, out var ranges))
                requirements[dependency] = ranges = new Dictionary<string, string>();
            ranges[name] = range;
            if (!candidates.TryGetValue(dependency, out var info) ||
                ranges.Values.Any(required => !PluginDependencyService.VersionInRange(info.PluginBaseInfo.Version, required)))
            {
                var staged = packages.FirstOrDefault(package => package.Info.ToPlgString() == dependency);
                var installed = AllPluginInfos.FirstOrDefault(item => item.ToPlgString() == dependency);
                if (staged is not null && installed is not null &&
                    ranges.Values.All(required => PluginDependencyService.VersionInRange(
                        installed.PluginBaseInfo.Version, required)))
                {
                    candidates[dependency] = installed;
                    staged.Dispose();
                    packages.Remove(staged);
                    return true;
                }
                var versions = await PluginNetworkService.GetVersionDetailsAsync(dependency, null, cancellationToken);
                var version = PluginDependencyService.SelectDependencyVersion(
                    versions?.Where(item => item.CanDownload &&
                        PluginNetworkService.SupportsCurrentPlatform(item.AvailablePlatforms) &&
                        (installed is null || PluginDependencyService.IsVersionNewer(
                            item.Version, installed.PluginBaseInfo.Version)))
                        .Select(item => item.Version) ?? [], ranges.Values.ToArray());
                if (version is null)
                    throw new InvalidOperationException($"找不到依赖 {dependency} 同时满足 {string.Join("、", ranges.Values)} 的可用更新版本。");
                var package = await DownloadPackageAsync(dependency, version, cancellationToken);
                packages.Add(package);
                candidates[dependency] = package.Info;
                if (staged is not null)
                {
                    staged.Dispose();
                    packages.Remove(staged);
                }
                return true;
            }
            if (await PrepareDependenciesAsync(info, candidates, packages, requirements, visited, visiting,
                    cancellationToken)) return true;
        }
        visiting.Remove(name);
        visited.Add(name);
        return false;
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
            HashSet<string> closure;
            var seenSelections = new HashSet<string>();
            while (true)
            {
                var selection = string.Join("|", candidates.OrderBy(item => item.Key)
                    .Select(item => $"{item.Key}={item.Value.PluginBaseInfo.Version}"));
                if (!seenSelections.Add(selection))
                    throw new InvalidOperationException("插件依赖版本选择无法收敛。");
                closure = new HashSet<string>();
                var changed = await PrepareDependenciesAsync(root, candidates, packages,
                    new Dictionary<string, Dictionary<string, string>>(), closure, new HashSet<string>(),
                    cancellationToken);
                if (!changed) break;
            }
            foreach (var package in packages.Where(package => !closure.Contains(package.Info.ToPlgString())).ToArray())
            {
                if (AllPluginInfos.FirstOrDefault(info => info.ToPlgString() == package.Info.ToPlgString()) is { } installed)
                    candidates[package.Info.ToPlgString()] = installed;
                else
                    candidates.Remove(package.Info.ToPlgString());
                package.Dispose();
                packages.Remove(package);
            }
            var toEnable = new HashSet<string>(runningBefore);
            if (enable) toEnable.UnionWith(closure);
            foreach (var target in toEnable.Concat(closure).Distinct())
                ValidateDependencies(candidates[target], candidates.Values, toEnable.Union(closure));
            var order = PluginDependencyService.TopologicalSort(candidates.Values.Where(info =>
                toEnable.Contains(info.ToPlgString())).ToList());
            cancellationToken.ThrowIfCancellationRequested();

            var replaced = packages.Select(package => GetPluginLocalInfoByPlgStr(package.Info.ToPlgString()))
                .Where(info => info is not null).Cast<PluginLocalInfo>().ToArray();
            var affected = new HashSet<PluginLocalInfo>();
            foreach (var old in replaced)
                foreach (var dependent in GetAffectedPlugins(old))
                    if (runningBefore.Contains(dependent.ToPlgString())) affected.Add(dependent);
            var stopOrder = PluginDependencyService.TopologicalSort(affected.ToList());
            stopOrder.Reverse();
            foreach (var dependent in stopOrder)
            {
                stopped.Add(dependent);
                if (!await UnloadCoreAsync(dependent))
                {
                    updatePending = true;
                    throw new InvalidOperationException(replacement is null
                        ? $"插件 {dependent.PluginBaseInfo.Name} 仍被引用，请重启后重试。"
                        : Lang.Format("lang.kitopia.messages.plugin_value_is_still_in_use_its_enabled_state_is_preserved_and_the_update_will_finish_after_restart", dependent.PluginBaseInfo.Name));
                }
            }
            foreach (var old in replaced.Where(info => !affected.Contains(info)))
            {
                if (!await UnloadCoreAsync(old))
                {
                    updatePending = true;
                    throw new InvalidOperationException(Lang.Format("lang.kitopia.messages.plugin_value_is_still_loaded_restart_to_update_it", old.PluginBaseInfo.Name));
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
                throw new IOException(Lang.Format("lang.kitopia.messages.plugin_value_directory_is_in_use_installation_will_be_retried_at_next_startup", replacement.Info.PluginBaseInfo.Name), exception);
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
        CancellationToken cancellationToken = default) =>
        RunOperationAsync(() => DownloadAndApplyAsync(pluginSign, targetVersion, true, cancellationToken));

    public static Task<bool> Update(string pluginSign, string? targetVersion = null,
        CancellationToken cancellationToken = default) => RunOperationAsync(async () =>
    {
        if (PluginReleaseRules.IsHostBundled(pluginSign) &&
            !PluginReleaseRules.IsBundledPluginUpdateAllowed(pluginSign))
            return false;
        if (GetPluginLocalInfoByPlgStr(pluginSign) is null) return false;
        var enable = EnablePlugins.ContainsKey(pluginSign) ||
                     ConfigManger.Config.EnabledPluginInfos.Any(info => info.NameSign == pluginSign);
        return await DownloadAndApplyAsync(pluginSign, targetVersion, enable, cancellationToken);
    });

    private static async Task<bool> DownloadAndApplyAsync(string pluginSign, string? targetVersion, bool enable,
        CancellationToken cancellationToken)
    {
        if (PluginReleaseRules.IsHostBundled(pluginSign) &&
            !PluginReleaseRules.IsBundledPluginUpdateAllowed(pluginSign)) return false;
        var info = GetPluginLocalInfoByPlgStr(pluginSign)?.PluginBaseInfo ?? new PluginBaseInfo
        {
            NameSign = pluginSign, Name = pluginSign, Version = targetVersion ?? string.Empty
        };
        ReportDownloadProgress(new PluginDownloadProgress(info, targetVersion));
        var online = await PluginNetworkService.GetOnlinePluginInfo(pluginSign, cancellationToken);
        targetVersion ??= online?.LastVersion;
        if (string.IsNullOrWhiteSpace(targetVersion)) return false;
        if (online is not null) info = online.ToPluginBaseInfo();
        ReportDownloadProgress(new PluginDownloadProgress(info, targetVersion));
        var package = await DownloadPackageAsync(pluginSign, targetVersion, cancellationToken);
        return await ApplyAsync(pluginSign, package, enable, cancellationToken);
    }

    private static async Task<PluginPackage> DownloadPackageAsync(string pluginSign, string version,
        CancellationToken cancellationToken)
    {
        var info = Downloads.GetValueOrDefault(pluginSign)?.PluginInfo ??
                   GetPluginLocalInfoByPlgStr(pluginSign)?.PluginBaseInfo ?? new PluginBaseInfo
                   {
                       NameSign = pluginSign, Name = pluginSign, Version = version
                   };
        var progress = new PluginDownloadProgress(info, version);
        ReportDownloadProgress(progress);
        var package = await PluginNetworkService.DownloadPackageAsync(pluginSign, version, cancellationToken,
            (downloaded, total) =>
            {
                progress = progress with
                {
                    DownloadedBytes = downloaded, TotalBytes = total,
                    IsInstalling = total is > 0 && downloaded >= total
                };
                ReportDownloadProgress(progress);
            });
        ReportDownloadProgress(progress with { PluginInfo = package.Info.PluginBaseInfo, IsInstalling = true });
        return package;
    }

    internal static void ReportDownloadProgress(PluginDownloadProgress progress)
    {
        if (progress.IsDownloading) ActiveDownloads[progress.PluginInfo.NameSign] = progress;
        else ActiveDownloads.TryRemove(progress.PluginInfo.NameSign, out _);
        WeakReferenceMessenger.Default.Send(progress);
    }

    public static void DisablePlugin(PluginLocalInfo info)
    {
        if (PluginReleaseRules.IsHostBundled(info.ToPlgString())) return;
        RequestRemoval(info, delete: false);
    }

    public static void DeletePlugin(PluginLocalInfo info)
    {
        if (PluginReleaseRules.IsHostBundled(info.ToPlgString())) return;
        RequestRemoval(info, delete: true);
    }
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
            Title = Lang.Format(delete ? "lang.kitopia.plugins.delete_title" : "lang.kitopia.plugins.disable_title", info.PluginBaseInfo.Name),
            Content = Lang.Format("lang.kitopia.plugins.affected", string.Join("\n", affected.Select(item => item.PluginBaseInfo.Name))),
            PrimaryButtonText = Lang.Get("lang.kitopia.ok"), CloseButtonText = Lang.Get("lang.kitopia.cancel"),
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
                ServiceManager.Services.GetService<IToastService>()?.Show(Lang.Get("lang.kitopia.plugin_unload_failed"),
                    Lang.Format("lang.kitopia.messages.plugin_value_is_still_in_use_restart_to_finish_value", info.PluginBaseInfo.Name, (delete ? Lang.Get("lang.kitopia.delete") : Lang.Get("lang.kitopia.deactivate"))));
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
