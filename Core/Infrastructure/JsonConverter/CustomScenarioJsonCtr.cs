using System.Text.Json;
using System.Text.Json.Serialization;
using Core.SDKs.CustomScenario;

namespace Core.Infrastructure.JsonConverter;

public class CustomScenarioJsonCtr : JsonConverter<CustomScenario.CustomScenario>
{
    public override CustomScenario.CustomScenario? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, CustomScenario.CustomScenario value, JsonSerializerOptions options)
    {
    }
}