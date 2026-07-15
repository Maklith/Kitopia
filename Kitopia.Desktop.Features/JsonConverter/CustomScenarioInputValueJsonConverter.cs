using System.Text.Json;
using System.Text.Json.Serialization;
using Kitopia.Desktop.Features.CustomScenario;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.JsonConverter;

public class CustomScenarioInputValueJsonConverter : JsonConverter<CustomScenarioValue>
{
    public override CustomScenarioValue? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        object value;
        var isSelf = false;
        Type serializeType = null;
        Type showType = null;
        while (reader.Read())
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var s = reader.GetString();
                switch (s)
                {
                    case "SerializeType":
                    {
                        reader.Read();
                        serializeType = new TypeJsonConverter().Read(ref reader, typeToConvert, options);
                        break;
                    }
                    case "ShowType":
                    {
                        reader.Read();

                        showType = new TypeJsonConverter().Read(ref reader, typeToConvert, options);
                        break;
                    }
                    case "IsSelf":
                    {
                        reader.Read();

                        isSelf = reader.GetBoolean();
                        break;
                    }
                    case "Value":
                    {
                        reader.Read();
                        if (reader.TokenType == JsonTokenType.Null)
                        {
                            reader.Read();
                            return new CustomScenarioValue
                            {
                                ShowType = showType,
                                SerializeType = serializeType,
                                IsSelf = isSelf
                            };
                        }

                        // if (reader.TokenType==JsonTokenType.StartObject|| reader.TokenType==JsonTokenType.StartArray)
                        // {
                        //     JsonSerializer.
                        // }
                        if (serializeType== typeof(object))
                        {
                            reader.Read();
                            return new CustomScenarioValue
                            {
                                SerializeType = serializeType,
                                ShowType = showType,
                                IsSelf = isSelf,
                                Value = null
                            };
                        }

                        if (serializeType==typeof(string))
                        {
                            var o = reader.GetString();
                            reader.Read();
                            return new CustomScenarioValue
                            {
                                SerializeType = serializeType,
                                ShowType = showType,
                                IsSelf = isSelf,
                                Value = o
                            };
                        }
                        if (CustomScenarioGlobe.JsonConverters.TryGetValue(serializeType, out var jsonConverter))
                        {
                            
                            // 获取 Read 方法
                            var readerValueSpan = reader.GetString();
                            reader.Read();
                            var deserialize = jsonConverter.Deserialize(readerValueSpan);
                            return new CustomScenarioValue
                            {
                                SerializeType = serializeType,
                                ShowType = showType,
                                Value = deserialize,
                                IsSelf = isSelf
                            };
                        }
                        if (serializeType.IsEnum)
                        {
                            var o = Enum.ToObject(serializeType, reader.GetInt32());
                            reader.Read();
                            return new CustomScenarioValue
                            {
                                SerializeType = serializeType,
                                ShowType = showType,
                                Value = o,
                                IsSelf = isSelf
                            };
                        }
                        if (isSelf)
                        {
                            throw new CustomScenarioLoadFromJsonException(
                                CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到, serializeType.FullName, null);
                        }

                        reader.Read();

                        return new CustomScenarioValue
                        {
                            SerializeType = serializeType,
                            ShowType = showType,
                            Value = null,
                            IsSelf = isSelf
                        };
                    }
                }
            }


        return null;
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

        if (value.SerializeType == typeof(string))
        {
            writer.WriteStringValue(value.Value?.ToString());
        }
        else if (CustomScenarioGlobe.JsonConverters.TryGetValue(value.SerializeType, out var jsonConverter))
        {
            var serialize = jsonConverter.Serialize(value.Value);
            writer.WriteStringValue(serialize);
        }
        else if (value.SerializeType.IsEnum)
        {
            var valueValue = (object)value.Value;
            writer.WriteNumberValue(Convert.ToInt32(valueValue));
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