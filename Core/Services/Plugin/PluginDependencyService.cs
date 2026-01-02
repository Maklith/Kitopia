using System.Collections.Concurrent;
using System.Text;
using Core.Services.Config;
using PluginCore;
using Serilog;

namespace Core.Services.Plugin;

public class PluginDependencyService
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

    private static readonly ILogger Log = LogManager.Logger.ForContext<PluginDependencyService>();

    public static bool VersionInRange(string version, string range)
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

    public static bool VersionInRange(Version v, string range)
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
    /// Checks dependencies for a set of plugins or a specific requirement.
    /// This method is pure and does not perform downloads or side effects.
    /// </summary>
    public static (bool CanLoad, ConcurrentDictionary<string, VersionCheckResult> Results) CheckDependencies(
        IEnumerable<PluginBaseInfo> availablePlugins, 
        Dictionary<string, string> dependencies, 
        IEnumerable<string> enabledPluginSignatures)
    {
        var results = new ConcurrentDictionary<string, VersionCheckResult>();
        var canLoad = true;
        var availableList = availablePlugins.ToList();
        var enabledSet = enabledPluginSignatures.ToHashSet();

        foreach (var (pluginSignName, verStr) in dependencies)
        {
            if (pluginSignName == "Kitopia")
            {
                if (!VersionInRange(ConfigManger.Version, verStr))
                {
                    canLoad = false;
                    results.TryAdd(pluginSignName, VersionCheckResult.Kitopia版本不匹配);
                }
                continue;
            }

            var pluginInfo = availableList.FirstOrDefault(e => e.NameSign == pluginSignName);
            if (pluginInfo.NameSign is null)
            {
                canLoad = false;
                results.TryAdd(pluginSignName, VersionCheckResult.依赖不存在);
                continue;
            }

            if (!VersionInRange(pluginInfo.Version, verStr))
            {
                canLoad = false;
                results.TryAdd(pluginSignName, VersionCheckResult.依赖版本不匹配);
                continue;
            }

            if (!enabledSet.Contains(pluginSignName))
            {
                // Dependency exists but is not enabled
                canLoad = false;
                results.TryAdd(pluginSignName, VersionCheckResult.依赖未启用);
                continue;
            }
        }

        return (canLoad, results);
    }
    
    public static (List<PluginLocalInfo> Sorted, List<PluginLocalInfo> Cyclic) SafeTopologicalSort(List<PluginLocalInfo> nodes)
    {
        var sorted = new List<PluginLocalInfo>();
        var nodeMap = nodes.ToDictionary(n => n.PluginBaseInfo.NameSign, n => n);
        var inDegree = nodes.ToDictionary(n => n.PluginBaseInfo.NameSign, n => 0);
        var adj = nodes.ToDictionary(n => n.PluginBaseInfo.NameSign, n => new List<string>());

        // 构建图和入度
        foreach (var node in nodes)
        {
            foreach (var dep in node.PluginBaseInfo.Dependencies)
            {
                if (dep.Key == "Kitopia") continue;

                if (nodeMap.ContainsKey(dep.Key))
                {
                    inDegree[node.PluginBaseInfo.NameSign]++;
                    adj[dep.Key].Add(node.PluginBaseInfo.NameSign);
                }
            }
        }

        var queue = new Queue<PluginLocalInfo>();
        foreach (var node in nodes)
        {
            if (inDegree[node.PluginBaseInfo.NameSign] == 0)
            {
                queue.Enqueue(node);
            }
        }

        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            sorted.Add(u);

            if (adj.TryGetValue(u.PluginBaseInfo.NameSign, out var neighbors))
            {
                foreach (var vName in neighbors)
                {
                    inDegree[vName]--;
                    if (inDegree[vName] == 0)
                    {
                        queue.Enqueue(nodeMap[vName]);
                    }
                }
            }
        }

        var sortedIds = sorted.Select(x => x.PluginBaseInfo.NameSign).ToHashSet();
        var cyclic = nodes.Where(x => !sortedIds.Contains(x.PluginBaseInfo.NameSign)).ToList();

        return (sorted, cyclic);
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
    
    public static void GetAllDependentPlugins(PluginLocalInfo target, IEnumerable<PluginLocalInfo> allPlugins, HashSet<PluginLocalInfo> collected)
    {
        var directDeps = allPlugins.Where(p => p.PluginBaseInfo.Dependencies.ContainsKey(target.PluginBaseInfo.NameSign));
        foreach (var dep in directDeps)
        {
            if (collected.Add(dep))
            {
                GetAllDependentPlugins(dep, allPlugins, collected);
            }
        }
    }
}
