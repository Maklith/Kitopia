using System.Text.Json;
using System.Text.Json.Serialization;
using Core.SDKs.CustomScenario;
using PluginCore;

namespace Core.JsonConverter;

public class INodeInputJsonConverter : JsonConverter<INodeInputConnector>
{
    public override INodeInputConnector? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        return null;
    }

    public override void Write(Utf8JsonWriter writer, INodeInputConnector value, JsonSerializerOptions options)
    {
    }
}