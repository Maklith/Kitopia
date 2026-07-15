using PluginCore;
using Serilog;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginDiscoveryService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<PluginDiscoveryService>();

    public static List<PluginLocalInfo> DiscoverPlugins(string pluginsPath, bool handleRemovals = false)
    {
        var candidates = new List<PluginLocalInfo>();
        var pluginsDirectoryInfo = new DirectoryInfo(pluginsPath);

        if (!pluginsDirectoryInfo.Exists)
        {
            Logger.Debug($"插件目录不存在创建{pluginsDirectoryInfo.FullName}");
            pluginsDirectoryInfo.Create();
            return candidates;
        }

        foreach (var directoryInfo in pluginsDirectoryInfo.EnumerateDirectories())
        {
            if (handleRemovals && File.Exists($"{directoryInfo.FullName}{Path.DirectorySeparatorChar}.remove"))
            {
                try
                {
                    directoryInfo.Delete(true);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "删除插件目录失败");
                }
                continue;
            }

            try
            {
                var manifestPath = Path.Combine(directoryInfo.FullName, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    var readAllText = File.ReadAllText(manifestPath);
                    var serialize = JsonSerializer.Deserialize<PluginBaseInfo?>(readAllText);
                    if (serialize != null)
                    {
                        var pluginBaseInfo = serialize.Value;
                        candidates.Add(new PluginLocalInfo
                        {
                            PluginBaseInfo = pluginBaseInfo,
                            FullPath = Path.Combine(directoryInfo.FullName, pluginBaseInfo.Main),
                            Path = directoryInfo.FullName + Path.DirectorySeparatorChar
                        });
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e, $"读取插件元数据错误: {directoryInfo.FullName}");
            }
        }

        return candidates;
    }
}
