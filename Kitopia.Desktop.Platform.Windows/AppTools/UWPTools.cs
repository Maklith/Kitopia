using System.Xml.Linq;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using PluginCore;
using Serilog;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Kitopia.Desktop.Platform.Windows.AppTools;

internal static class UwpTools
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext(typeof(UwpTools));

    internal static void GetAll(SearchIndex index)
    {
        IEnumerable<Package> packages;
        try
        {
            packages = new PackageManager().FindPackagesForUser(string.Empty);
        }
        catch (Exception exception)
        {
            Logger.Error(exception, "枚举当前用户的已安装应用包失败");
            return;
        }

        foreach (var package in packages)
        {
            try
            {
                IndexPackage(package, index);
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "索引应用包 {PackageFamilyName} 失败", package.Id.FamilyName);
            }
        }
    }

    private static void IndexPackage(Package package, SearchIndex index)
    {
        if (package.IsFramework || package.IsResourcePackage)
            return;

        var packageFamilyName = package.Id.FamilyName;
        IReadOnlyDictionary<string, string?> iconPaths;
        try
        {
            iconPaths = ReadApplicationIconPaths(package.InstalledPath);
        }
        catch (Exception exception)
        {
            Logger.Debug(exception, "读取应用包 {PackageFamilyName} 的图标失败", packageFamilyName);
            iconPaths = new Dictionary<string, string?>();
        }

        foreach (var app in package.GetAppListEntries())
        {
            try
            {
                var appUserModelId = app.AppUserModelId;
                if (string.IsNullOrWhiteSpace(appUserModelId))
                    continue;

                if (ConfigManger.Config.ignoreItems.Contains(appUserModelId) ||
                    ConfigManger.Config.ignoreItems.Contains(packageFamilyName))
                {
                    Logger.Debug("忽略索引:{AppUserModelId}", appUserModelId);
                    continue;
                }

                var displayName = app.DisplayInfo.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = package.DisplayName;
                if (string.IsNullOrWhiteSpace(displayName))
                    continue;

                var applicationId = GetApplicationId(appUserModelId);
                iconPaths.TryGetValue(applicationId, out var iconPath);

                index.TryAdd(new SearchEntry
                {
                    DisplayName = displayName,
                    OnlyKey = appUserModelId,
                    FileType = FileType.UWP应用,
                    IconPath = iconPath
                });
            }
            catch (Exception exception)
            {
                Logger.Warning(exception, "索引应用包 {PackageFamilyName} 中的一个启动项失败", packageFamilyName);
            }
        }
    }

    private static string GetApplicationId(string appUserModelId)
    {
        var separatorIndex = appUserModelId.IndexOf('!');
        return separatorIndex >= 0 ? appUserModelId[(separatorIndex + 1)..] : appUserModelId;
    }

    internal static IReadOnlyDictionary<string, string?> ReadApplicationIconPaths(string packageDirectory)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var manifestPath = Path.Combine(packageDirectory, "AppxManifest.xml");
        if (!File.Exists(manifestPath))
            return result;

        var document = XDocument.Load(manifestPath);
        foreach (var application in document.Descendants().Where(element => element.Name.LocalName == "Application"))
        {
            var applicationId = GetAttributeValue(application, "Id");
            if (string.IsNullOrWhiteSpace(applicationId))
                continue;

            var visualElements = application.Elements()
                .FirstOrDefault(element => element.Name.LocalName.EndsWith("VisualElements", StringComparison.Ordinal));
            var logo = GetAttributeValue(visualElements, "Square44x44Logo")
                       ?? GetAttributeValue(visualElements, "Square30x30Logo")
                       ?? GetAttributeValue(visualElements, "Square150x150Logo")
                       ?? GetAttributeValue(visualElements, "Logo")
                       ?? GetAttributeValue(application, "Logo");

            var executable = GetAttributeValue(application, "Executable");
            result[applicationId] = ResolveAssetPath(packageDirectory, logo)
                                    ?? ResolveAssetPath(packageDirectory, executable);
        }

        return result;
    }

    private static string? GetAttributeValue(XElement? element, string localName)
    {
        return element?.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName == localName)
            ?.Value;
    }

    internal static string? ResolveAssetPath(string packageDirectory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
            return null;

        const string msAppxPrefix = "ms-appx:///";
        if (relativePath.StartsWith(msAppxPrefix, StringComparison.OrdinalIgnoreCase))
            relativePath = relativePath[msAppxPrefix.Length..];

        var normalizedPath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var exactPath = Path.Combine(packageDirectory, normalizedPath);
        if (File.Exists(exactPath))
            return exactPath;

        var directory = Path.GetDirectoryName(exactPath);
        var fileName = Path.GetFileNameWithoutExtension(exactPath);
        var extension = Path.GetExtension(exactPath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, $"{fileName}*{extension}", SearchOption.TopDirectoryOnly)
            .OrderBy(GetAssetPreference)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int GetAssetPreference(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Contains("targetsize-44", StringComparison.OrdinalIgnoreCase) &&
            name.Contains("altform-unplated", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Contains("targetsize-44", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (name.Contains("scale-200", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (name.Contains("scale-100", StringComparison.OrdinalIgnoreCase))
            return 3;
        return 4;
    }
}
