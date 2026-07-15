#region

using System.Xml;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Search;
using PluginCore;
using Serilog;
using Vanara.Extensions;
using Vanara.PInvoke;
using Vanara.Windows.Shell;

#endregion

namespace Kitopia.Desktop.Platform.Windows.AppTools;

internal class UwpTools
{
    private static readonly HashSet<string> ErrorUwPs = new();
    private static readonly ILogger Logger = LogManager.Logger.ForContext<UwpTools>();

    private static XmlNode? GetApplicationNode(XmlNode node)
    {
        foreach (XmlNode o in node.ChildNodes)
        {
            if (o.Name == "Application") return o;

            var nodes = GetApplicationNode(o);
            if (nodes is not null) return nodes;
        }

        return null;
    }

    internal static void GetAll(SearchIndex index)
    {
        FirewallApi.NetworkIsolationEnumAppContainers(FirewallApi.NETISO_FLAG.NETISO_FLAG_FORCE_COMPUTE_BINARIES,
            out var pdwNuminternalAppCs, out var ppinternalAppCs);
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 5
        };
        Parallel.ForEach(ppinternalAppCs.ToIEnum<FirewallApi.INET_FIREWALL_APP_CONTAINER>(
            (int)pdwNuminternalAppCs), options, file =>
        {
            try
            {
                if (!ErrorUwPs.Contains(file.displayName)) AppContainerAnalyse(file, index);
            }
            catch (Exception e)
            {
                Logger.Error(e, "UWP索引时出现错误");
                ErrorUwPs.Add(file.displayName);
            }
        });
    }

    private static void AppContainerAnalyse(FirewallApi.INET_FIREWALL_APP_CONTAINER appContainer,
        SearchIndex index)
    {
        if (ConfigManger.Config.ignoreItems.Contains(appContainer.appContainerName))
        {
            Logger.Debug("忽略索引:" + appContainer.appContainerName);
            return;
        }

        if (string.IsNullOrWhiteSpace(appContainer.appContainerName) ||
            string.IsNullOrWhiteSpace(appContainer.displayName) ||
            string.IsNullOrWhiteSpace(appContainer.workingDirectory))
            return;

        var fileName = appContainer.displayName;
        try
        {
            fileName = new IndirectString(appContainer.displayName).Value;
        }
        catch (Exception e)
        {
            Logger.Error($"错误的UWP应用{appContainer.displayName}:{e.Message}");
            ErrorUwPs.Add(appContainer.displayName);
        }

        if (string.IsNullOrWhiteSpace(fileName)) return;


        var xmlDocument = new XmlDocument();
        if (File.Exists($"{appContainer.workingDirectory}{Path.DirectorySeparatorChar}AppxManifest.xml"))
            xmlDocument.Load($"{appContainer.workingDirectory}{Path.DirectorySeparatorChar}AppxManifest.xml");
        else if (File.Exists($"{appContainer.workingDirectory}{Path.DirectorySeparatorChar}appxmanifest.xml"))
            xmlDocument.Load($"{appContainer.workingDirectory}{Path.DirectorySeparatorChar}appxmanifest.xml");
        else
            return;

        var application = GetApplicationNode(xmlDocument);

        if (application?.Attributes == null) return;

        var applicationAttribute = application.Attributes["Id"];
        if (applicationAttribute is null) return;

        var id = applicationAttribute.Value;
        XmlNode? visualElements = null;
        foreach (XmlNode applicationChildNode in application.ChildNodes)
            if (applicationChildNode.Name.Contains("VisualElements"))
                visualElements = applicationChildNode;

        if (visualElements == null) return;

        if (visualElements.Attributes == null) return;

        var visualElementsAttribute = visualElements.Attributes["Square44x44Logo"];


        if (visualElementsAttribute == null) return;

        var squareLogo = visualElementsAttribute.Value;
        var logoName = squareLogo.Split(Path.DirectorySeparatorChar)
            .Last()
            .Split(".")
            .First();
        var path = $"{appContainer.workingDirectory}{squareLogo.Split(Path.DirectorySeparatorChar).First()}";
        if (!Directory.Exists(path)) return;

        var logos =
            new DirectoryInfo(path);
        var pa = $"{path}{Path.DirectorySeparatorChar}{logoName}.scale-200.png";
        if (File.Exists(pa))
        {
            index.TryAdd(new SearchEntry
            {
                DisplayName = fileName,
                OnlyKey = $"{appContainer.appContainerName}!{id}",
                FileType = FileType.UWP应用,
                IconPath = pa
            });
            return;
        }

        {
            foreach (var enumerateFile in logos.EnumerateFiles())
                if (enumerateFile.Name.StartsWith(logoName))
                {
                    index.TryAdd(new SearchEntry
                    {
                        DisplayName = fileName,
                        OnlyKey = $"{appContainer.appContainerName}!{id}",
                        FileType = FileType.UWP应用,
                        IconPath = enumerateFile.FullName
                    });
                    break;
                }
        }
    }
}
