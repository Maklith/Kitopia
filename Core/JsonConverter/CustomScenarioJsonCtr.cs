using System.Text.Json;
using Core.SDKs.CustomScenario;

namespace Core.JsonConverter;

public class CustomScenarioJsonCtr : System.Text.Json.Serialization.JsonConverter<CustomScenario>
{
    public override CustomScenario? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, CustomScenario value, JsonSerializerOptions options)
    {
    }
}