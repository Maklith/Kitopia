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
        if (PluginManager.EnablePlugin.TryGetValue(strings[0], out var value))
        {
            return value.GetType(strings[1]) ??
                   throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.类未找到, strings[0],
                       strings[1]);
            ;
        }

        throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未找到, strings[0],
            strings[1]);
    }

    public PluginInfo? GetPluginInfo(Type type)
    {
        var firstOrDefault = PluginManager.EnablePlugin.FirstOrDefault((e) => e.Value.IsPluginAssembly(type.Assembly));
        if (firstOrDefault.Value is null) return null;
        return firstOrDefault.Value.PluginInfo;
    }
}