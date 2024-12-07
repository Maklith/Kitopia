using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.SDKs.CustomScenario;
using PluginCore;

namespace Core.JsonConverter;

public class CustomScenarioInputValueJsonConverter : JsonConverter<CustomScenarioValue>
{
    public override CustomScenarioValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        
        object value;
        System.Type type=null;
        Type realType = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var s = reader.GetString();
                switch (s)
                {
                    case "Type":
                    {
                        reader.Read();
                        type= new TypeJsonConverter().Read(ref reader, typeToConvert, options);
                        break;
                    }
                    case "RealType":
                    {
                        reader.Read();
                    
                        realType= new TypeJsonConverter().Read(ref reader, typeToConvert, options);
                        break;
                    }
                    case "Value":
                    {
                        reader.Read();
                        if (reader.TokenType==JsonTokenType.Null)
                        {
                            reader.Read();
                            return new CustomScenarioValue
                            {
                                RealType = realType,
                                Type = type
                            };
                        }

                        // if (reader.TokenType==JsonTokenType.StartObject|| reader.TokenType==JsonTokenType.StartArray)
                        // {
                        //     JsonSerializer.
                        // }
                        if (type.FullName==typeof(object).FullName)
                        {
                            reader.Read();
                            return new CustomScenarioValue
                            {
                                Type = type,
                                RealType = realType,
                                Value = null
                            };
                        }else if (CustomScenarioGloble.JsonConverters.ContainsKey(realType))
                        {
                            var jsonConverter = CustomScenarioGloble.JsonConverters[type];
                            // 获取 Read 方法
                            var readerValueSpan = reader.ValueSpan;
                            reader.Read();
                            var deserialize = jsonConverter.Deserialize(readerValueSpan);
                            return new CustomScenarioValue
                            {
                                Type = type,
                                RealType = realType,
                                Value = deserialize
                            };
                        }
                        else
                        {
                            throw new CustomScenarioLoadFromJsonException(
                                CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到, type.FullName, null);
                        }

                        break;
                    }
                }
            }
        }
      

        return null;
    }

    public override void Write(Utf8JsonWriter writer, CustomScenarioValue value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("Type");
        var typeJsonConverter = new TypeJsonConverter();
        typeJsonConverter.Write(writer,value.Type,options);
        writer.WritePropertyName("RealType");
        typeJsonConverter.Write(writer,value.RealType,options);
        writer.WritePropertyName("Value");
        if (value.RealType.FullName==typeof(object).FullName)
        {
            writer.WriteStringValue("");
        }else if (CustomScenarioGloble.JsonConverters.ContainsKey(value.RealType))
        {
            var jsonConverter = CustomScenarioGloble.JsonConverters[value.RealType];
            var serialize = jsonConverter.Serialize(value.Value);
            writer.WriteStringValue(serialize);
        }
        else {
            throw new CustomScenarioLoadFromJsonException(
                CustomScenarioLoadFromJsonFailedType.类的序列化转换器未找到, value.RealType.FullName, null);
        }
        writer.WriteEndObject();
    }
}