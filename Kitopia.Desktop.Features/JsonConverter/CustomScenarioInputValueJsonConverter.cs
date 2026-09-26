using System.Text.Json;
using System.Text.Json.Serialization;
using Kitopia.Desktop.Features.CustomScenario;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.JsonConverter;

public class CustomScenarioInputValueJsonConverter : JsonConverter<CustomScenarioValue>
{
    private static readonly JsonSerializerOptions TypeOptions = new()
    {
        Converters = { new TypeJsonConverter() }
    };

    public override CustomScenarioValue? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var json = document.RootElement;
        if (!json.TryGetProperty("SerializeType", out var serializedType))
            throw new JsonException("情景值缺少 SerializeType。");

        var serializeType = serializedType.Deserialize<Type>(TypeOptions)!;
        var showType = json.TryGetProperty("ShowType", out var shownType)
            ? shownType.Deserialize<Type>(TypeOptions)!
            : serializeType;
        var isSelf = json.TryGetProperty("IsSelf", out var self) && self.GetBoolean();
        object? value = null;
        if (json.TryGetProperty("Value", out var storedValue) && storedValue.ValueKind != JsonValueKind.Null)
        {
            if (serializeType == typeof(string))
                value = storedValue.GetString();
            else if (serializeType == typeof(object))
                value = null;
            else if (CustomScenarioGlobe.JsonConverters.TryGetValue(serializeType, out var converter))
                value = converter.Deserialize(storedValue.GetString());
            else if (serializeType.IsEnum)
                value = Enum.ToObject(serializeType, storedValue.GetInt32());
            else if (isSelf)
                throw new CustomScenarioLoadFromJsonException(
                    CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到,
                    serializeType.FullName!, null);
        }

        return new CustomScenarioValue
        {
            SerializeType = serializeType,
            ShowType = showType,
            IsSelf = isSelf,
            Value = value
        };
    }

    public override void Write(Utf8JsonWriter writer, CustomScenarioValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("IsSelf");
        writer.WriteBooleanValue(value.IsSelf);
        writer.WritePropertyName("SerializeType");
        var typeJsonConverter = new TypeJsonConverter();
        typeJsonConverter.Write(writer, value.SerializeType, options);
        writer.WritePropertyName("ShowType");
        typeJsonConverter.Write(writer, value.ShowType, options);
        writer.WritePropertyName("Value");
        if (value.SerializeType == typeof(object))
        {
            writer.WriteStringValue("");
        }
        else if (value.SerializeType == typeof(string))
        {
            writer.WriteStringValue(value.Value?.ToString());
        }
        else if (CustomScenarioGlobe.JsonConverters.TryGetValue(value.SerializeType, out var jsonConverter))
        {
            if (value.Value is null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(jsonConverter.Serialize(value.Value));
        }
        else if (value.SerializeType.IsEnum)
        {
            if (value.Value is null)
                writer.WriteNullValue();
            else
                writer.WriteNumberValue(Convert.ToInt32(value.Value));
        }
        else if (value.IsSelf)
        {
            throw new CustomScenarioLoadFromJsonException(
                CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到, value.SerializeType.FullName!, null);
        }
        else
        {
            writer.WriteNullValue();
        }

        writer.WriteEndObject();
    }
}
