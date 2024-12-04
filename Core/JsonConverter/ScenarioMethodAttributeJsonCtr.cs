using System.Text.Json;
using System.Text.Json.Serialization;
using PluginCore.Attribute;

namespace Core.SDKs.Services.Config;

public class ScenarioMethodAttributeJsonCtr : JsonConverter<ScenarioMethodAttribute>
{
    public override ScenarioMethodAttribute? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        // 创建一个 ScenarioMethodAttribute 实例
        ScenarioMethodAttribute attribute = new ScenarioMethodAttribute();

        // 确保 JSON 数据以对象开始
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        // 读取 JSON 对象的属性
        while (reader.Read())
        {
            // 检查是否已经到达对象的结尾
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return attribute;
            }

            // 读取属性名称
            string propertyName = reader.GetString();
            reader.Read(); // 进入属性值

            // 根据属性名称将值分配给属性
            switch (propertyName)
            {
                case "Name":
                    attribute.Name = reader.GetString();
                    break;
                case "ParameterName":
                    attribute.ParameterName =
                        JsonSerializer.Deserialize<Dictionary<string, string>>(ref reader, options);
                    break;
                default:
                    // 跳过未处理的属性
                    reader.Skip();
                    break;
            }
        }

        return attribute;
    }

    public override void Write(Utf8JsonWriter writer, ScenarioMethodAttribute value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("Name", value.Name);
        writer.WritePropertyName("ParameterName");
        JsonSerializer.Serialize(writer, value.ParameterName, value.ParameterName.GetType(), options);
        writer.WriteEndObject();
    }
}