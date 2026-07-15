using System.Reflection;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;

namespace Kitopia.Desktop.Features.PluginHost.Services;

public sealed class CustomScenarioPluginIntegration : ICustomScenarioPluginIntegration
{
    public bool IsPluginInstalled(string pluginSign)
    {
        return PluginManager.GetPluginLocalInfoByPlgStr(pluginSign) is not null;
    }

    public bool IsPluginEnabled(string pluginSign)
    {
        return PluginManager.GetPluginLocalInfoOnlyOnEnableByPlgStr(pluginSign) is not null;
    }

    public CustomScenarioPluginDescriptor? GetInstalledPlugin(string pluginSign)
    {
        var plugin = PluginManager.GetPluginLocalInfoByPlgStr(pluginSign);
        return plugin is null ? null : ToDescriptor(plugin.PluginBaseInfo.Id, plugin.PluginBaseInfo.Name,
            plugin.PluginBaseInfo.NameSign, plugin.PluginBaseInfo.VersionId);
    }

    public async Task<CustomScenarioPluginDescriptor?> GetOnlinePluginAsync(
        int pluginId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugin = await PluginNetworkService.GetOnlinePluginInfo(pluginId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return plugin is null
            ? null
            : ToDescriptor(plugin.Id, plugin.Name, plugin.NameSign, plugin.LastVersionId);
    }

    public async Task<bool> DownloadAndEnableAsync(
        int pluginId,
        string pluginSign,
        int? versionId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await PluginManager.DownloadPluginAndEnable(pluginId, pluginSign, versionId)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public void EnablePlugin(string pluginSign)
    {
        PluginManager.EnablePlugin(pluginSign);
    }

    public IServiceProvider GetServiceProvider(string pluginSign)
    {
        return PluginManager.GetServiceProvider(pluginSign);
    }

    public MethodInfo GetMethodInfo(string pluginSign, string methodAbsolutelyName)
    {
        return PluginManager.GetMethodInfo(pluginSign, methodAbsolutelyName);
    }

    private static CustomScenarioPluginDescriptor ToDescriptor(
        int id,
        string name,
        string nameSign,
        int versionId)
    {
        return new CustomScenarioPluginDescriptor(id, name, nameSign, versionId);
    }
}
