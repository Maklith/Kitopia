using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.SDKs.CustomScenario;
using Core.SDKs.Services.Plugin;

namespace Core.JsonConverter;

public class ScenarioMethodJsonCtr : JsonConverter<ScenarioMethod>
{
    public override ScenarioMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var scenario = JsonSerializer.Deserialize<ScenarioMethod>(ref reader, options)!;
        if (scenario.IsFromPlugin)
        {
            if (!PluginManager.EnablePlugin.ContainsKey(scenario.PluginInfo!.ToPlgString()))
            {
                if (PluginManager.AllPluginInfos.Any(e => e.ToPlgString() == scenario.PluginInfo!.ToPlgString()))
                    throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未启用,
                        scenario.PluginInfo.ToPlgString(), null);
                throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未找到,
                    scenario.PluginInfo.ToPlgString(), null);
            }

            scenario.ServiceProvider = PluginManager.EnablePlugin[scenario.PluginInfo!.ToPlgString()].ServiceProvider!;
            scenario.Method = PluginManager.EnablePlugin[scenario.PluginInfo.ToPlgString()]
                .GetMethod(scenario._methodAbsolutelyName);
        }

        return scenario;
    }

    public override void Write(Utf8JsonWriter writer, ScenarioMethod value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}