using NuGet.Versioning;
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

        if (handleRemovals)
        {
            foreach (var removed in pluginsDirectoryInfo.EnumerateDirectories(".remove-*"))
            {
                var suffix = removed.Name.LastIndexOf('-');
                if (suffix <= ".remove-".Length || !Guid.TryParseExact(removed.Name[(suffix + 1)..], "N", out _)) continue;
                try { removed.Delete(true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                { Logger.Warning(exception, "清理已移出的插件目录失败：{Path}", removed.FullName); }
            }
            foreach (var backup in pluginsDirectoryInfo.EnumerateDirectories(".backup-*"))
            {
                // Recover a crash between moving the old directory aside and activating the new one.
                var suffix = backup.Name.LastIndexOf('-');
                if (suffix <= ".backup-".Length || !Guid.TryParseExact(backup.Name[(suffix + 1)..], "N", out _)) continue;
                var originalName = backup.Name[".backup-".Length..suffix];
                try
                {
                    ValidatePluginSign(originalName);
                    var target = Path.Combine(pluginsPath, originalName);
                    var rollback = Path.Combine(target, ".rollback");
                    if (Directory.Exists(target) &&
                        (!File.Exists(rollback) || File.ReadAllText(rollback).Trim() != backup.Name)) continue;
                    _ = ReadPlugin(backup.FullName);
                    if (Directory.Exists(target)) RemovePluginDirectory(target);
                    Directory.Move(backup.FullName, target);
                    Logger.Warning("已恢复中断的插件安装：{Plugin}", originalName);
                }
                catch (Exception exception) { Logger.Error(exception, "恢复插件备份失败：{Path}", backup.FullName); }
            }
        }

        foreach (var directoryInfo in pluginsDirectoryInfo.EnumerateDirectories())
        {
            if (directoryInfo.Name.StartsWith('.')) continue;
            if (handleRemovals && File.Exists(Path.Combine(directoryInfo.FullName, ".remove")) &&
                !File.Exists(Path.Combine(directoryInfo.FullName, ".update")))
            {
                try
                {
                    RemovePluginDirectory(directoryInfo.FullName);
                }
                catch (Exception e)
                {
                    Logger.Error(e, "删除插件目录失败");
                }
                continue;
            }

            try
            {
                if (File.Exists(Path.Combine(directoryInfo.FullName, "manifest.json")))
                    candidates.Add(ReadPlugin(directoryInfo.FullName));
            }
            catch (Exception e)
            {
                Logger.Error(e, $"读取插件元数据错误: {directoryInfo.FullName}");
            }
        }

        var duplicates = candidates.GroupBy(plugin => plugin.ToPlgString(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in duplicates) Logger.Error("存在重复插件标识 {Plugin}，已跳过这些目录", duplicate);
        candidates.RemoveAll(plugin => duplicates.Contains(plugin.ToPlgString()));
        return candidates;
    }

    internal static void RemovePluginDirectory(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var removed = Path.Combine(Path.GetDirectoryName(directory)!,
            $".remove-{Path.GetFileName(directory)}-{Guid.NewGuid():N}");
        // A locked DLL must not leave a live plugin directory with its manifest already deleted.
        Directory.Move(directory, removed);
        try { Directory.Delete(removed, true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Logger.Warning(exception, "插件已移出，剩余文件将在下次启动时清理：{Path}", removed); }
    }

    internal static PluginLocalInfo ReadPlugin(string directory)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")))
                       ?? throw new InvalidDataException("插件清单为空。");
        ValidatePluginSign(manifest.NameSign);
        if (!NuGetVersion.TryParse(manifest.Version, out _))
            throw new InvalidDataException($"插件版本无效：{manifest.Version}");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        var main = Path.GetFullPath(Path.Combine(root, manifest.Main));
        if (!main.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(main))
            throw new InvalidDataException($"插件入口无效：{manifest.Main}");
        _ = System.Reflection.AssemblyName.GetAssemblyName(main);
        if (manifest.Dependencies is null) throw new InvalidDataException("插件依赖不能为空。");
        foreach (var (dependency, requirement) in manifest.Dependencies)
        {
            var range = requirement?.Trim();
            // Convert unambiguous legacy caret declarations at the manifest boundary.
            // All runtime dependency checks use NuGet ranges.
            if (range?.StartsWith('^') == true && NuGetVersion.TryParse(range[1..], out var minimum))
            {
                var upper = minimum.Major > 0 ? new NuGetVersion(minimum.Major + 1, 0, 0)
                    : minimum.Minor > 0 ? new NuGetVersion(0, minimum.Minor + 1, 0)
                    : new NuGetVersion(0, 0, minimum.Patch + 1);
                range = dependency == "Kitopia" && range == "^0.0.0"
                    ? "*"
                    : new VersionRange(minimum, true, upper, false).ToNormalizedString();
                manifest.Dependencies[dependency] = range;
            }
            if (!VersionRange.TryParse(range, out _))
                throw new InvalidDataException($"插件 {manifest.NameSign} 的依赖 {dependency} 版本范围无效：{requirement}。请使用 NuGet 语法，例如 [1.0.0,2.0.0)。");
        }
        return new PluginLocalInfo { PluginBaseInfo = manifest.ToPluginBaseInfo(), Path = root, FullPath = main };
    }

    internal static void ValidatePluginSign(string sign)
    {
        if (string.IsNullOrWhiteSpace(sign) || sign.StartsWith('.') || sign != Path.GetFileName(sign) ||
            sign.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || sign.Contains('/') || sign.Contains('\\'))
            throw new InvalidDataException($"插件标识无效：{sign}");
    }
}
