using System.Text.Json;
using System.Text.Json.Serialization;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.JsonConverter;

public class ScenarioMethodJsonCtr : JsonConverter<ScenarioMethod>
{
    public override ScenarioMethod Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var scenario = JsonSerializer.Deserialize<ScenarioMethod>(ref reader, options)!;
        if (scenario.IsFromPlugin)
        {
            var pluginIntegration = ServiceManager.Services
                .GetRequiredService<ICustomScenarioPluginIntegration>();
            var pluginSign = scenario.PluginInfo!.ToPlgString();
            if (!pluginIntegration.IsPluginEnabled(pluginSign))
            {
                if (pluginIntegration.IsPluginInstalled(pluginSign))
                    throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未启用,
                        pluginSign, null);
                throw new CustomScenarioLoadFromJsonException(CustomScenarioLoadFromJsonFailedType.插件未找到,
                    pluginSign, null);
            }

            scenario.ServiceProvider = pluginIntegration.GetServiceProvider(pluginSign);
            scenario.Method =
                pluginIntegration.GetMethodInfo(pluginSign, scenario._methodAbsolutelyName);
        }

        return scenario;
    }

    public override void Write(Utf8JsonWriter writer, ScenarioMethod value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
