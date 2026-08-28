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
        return plugin is null ? null : ToDescriptor(
            plugin.PluginBaseInfo.Name,
            plugin.PluginBaseInfo.NameSign,
            plugin.PluginBaseInfo.Version);
    }

    public async Task<CustomScenarioPluginDescriptor?> GetOnlinePluginAsync(
        string pluginSign,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugin = await PluginNetworkService.GetOnlinePluginInfo(pluginSign, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return plugin is null
            ? null
            : ToDescriptor(plugin.Name, plugin.NameSign, plugin.LastVersion ?? string.Empty);
    }

    public async Task<bool> DownloadAndEnableAsync(
        string pluginSign,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await PluginManager.DownloadPluginAndEnable(pluginSign, version)
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
        string name,
        string nameSign,
        string version)
    {
        return new CustomScenarioPluginDescriptor(name, nameSign, version);
    }
}
