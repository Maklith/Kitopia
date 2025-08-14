using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;

namespace Core.SDKs.Services.Config;

public class PointJsonConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var typeName = (string)reader.GetString();
        return Point.Parse(typeName);
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}