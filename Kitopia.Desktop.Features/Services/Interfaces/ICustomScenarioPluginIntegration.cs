using System.Reflection;

namespace Kitopia.Desktop.Features.Services.Interfaces;

public sealed record CustomScenarioPluginDescriptor(
    string Name,
    string NameSign,
    string Version);

public interface ICustomScenarioPluginIntegration
{
    bool IsPluginInstalled(string pluginSign);
    bool IsPluginEnabled(string pluginSign);
    CustomScenarioPluginDescriptor? GetInstalledPlugin(string pluginSign);
    Task<CustomScenarioPluginDescriptor?> GetOnlinePluginAsync(
        string pluginSign,
        CancellationToken cancellationToken = default);
    Task<bool> DownloadAndEnableAsync(
        string pluginSign,
        string? version = null,
        CancellationToken cancellationToken = default);
    void EnablePlugin(string pluginSign);
    IServiceProvider GetServiceProvider(string pluginSign);
    MethodInfo GetMethodInfo(string pluginSign, string methodAbsolutelyName);
}
