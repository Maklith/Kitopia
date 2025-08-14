using System.Text.Json;
using System.Text.Json.Serialization;
using Core.CustomScenario;
using Core.SDKs.CustomScenario;
using Core.Services.Plugin;

namespace Core.Infrastructure.JsonConverter;

public class ScenarioMethodJsonCtr : JsonConverter<ScenarioMethod>
{
    public override ScenarioMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var scenario = JsonSerializer.Deserialize<ScenarioMethod>(ref reader, options)!;
        if (scenario.IsFromPlugin)
        {
            if (PluginManager.GetPluginLocalInfoOnlyOnEnableByPlgStr(scenario.PluginInfo!.ToPlgString()) is null)
            {
                if (PluginManager.GetPluginLocalInfoByPlgStr(scenario.PluginInfo!.ToPlgString()) is not null)
                    throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未启用,
                        scenario.PluginInfo.ToPlgString(), null);
                throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未找到,
                    scenario.PluginInfo.ToPlgString(), null);
            }

            scenario.ServiceProvider = PluginManager.GetServiceProvider(scenario.PluginInfo!.ToPlgString());
            scenario.Method =
                PluginManager.GetMethodInfo(scenario.PluginInfo!.ToPlgString(), scenario._methodAbsolutelyName);
        }

        return scenario;
    }

    public override void Write(Utf8JsonWriter writer, ScenarioMethod value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}