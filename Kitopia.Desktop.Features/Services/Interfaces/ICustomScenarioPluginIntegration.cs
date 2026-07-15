using System.Reflection;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public sealed record CustomScenarioPluginDescriptor(
    int Id,
    string Name,
    string NameSign,
    int VersionId);

public interface ICustomScenarioPluginIntegration
{
    bool IsPluginInstalled(string pluginSign);
    bool IsPluginEnabled(string pluginSign);
    CustomScenarioPluginDescriptor? GetInstalledPlugin(string pluginSign);
    Task<CustomScenarioPluginDescriptor?> GetOnlinePluginAsync(
        int pluginId,
        CancellationToken cancellationToken = default);
    Task<bool> DownloadAndEnableAsync(
        int pluginId,
        string pluginSign,
        int? versionId = null,
        CancellationToken cancellationToken = default);
    void EnablePlugin(string pluginSign);
    IServiceProvider GetServiceProvider(string pluginSign);
    MethodInfo GetMethodInfo(string pluginSign, string methodAbsolutelyName);
}
