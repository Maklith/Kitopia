using System;
using Kitopia.Desktop.Features.Services.Plugin;
using PluginCore;

namespace Kitopia.Desktop.Features.PluginHost.Services;

public sealed class PluginMangerService : IPluginManger
{
    public Type GetType(string[] strings)
    {
        return PluginManager.GetType(strings);
    }

    public PluginBaseInfo? GetPluginInfo(Type type)
    {
        var firstOrDefault = PluginManager.GetPluginBaseInfoByType(type);
        if (firstOrDefault is null) return null;
        return firstOrDefault.Value;
    }

    public bool IsTypeFromThePlugin(Type type, string pluginName)
    {
        if (type is null) return false;
        if (string.IsNullOrEmpty(pluginName)) return false;

        // Check if the type is from the specified plugin
        return PluginManager.IsTypeFromThePlugin(type, pluginName);
    }
}
