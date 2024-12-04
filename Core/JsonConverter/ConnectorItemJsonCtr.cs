using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Core.SDKs.CustomScenario;

namespace Core.SDKs.Services.Config;

public class ConnectorItemJsonCtr : JsonConverter<ConnectorItem>
{
    public override ConnectorItem? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        ConnectorItem connectorItem = JsonSerializer.Deserialize<ConnectorItem>(ref reader, new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,

            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            //DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });

        return connectorItem;
    }

    public override void Write(Utf8JsonWriter writer, ConnectorItem value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,

            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            //DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}