using System;
using Core.Services.Plugin;
using PluginCore;

namespace KitopiaAvalonia.Services;

public class PluginMangerService : IPluginManger
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