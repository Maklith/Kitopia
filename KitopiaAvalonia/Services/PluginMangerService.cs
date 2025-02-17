using System;
using System.Linq;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services.Plugin;
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
}