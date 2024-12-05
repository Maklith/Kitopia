using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.SDKs.CustomScenario;

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
                                Type = type
                            };
                        }

                        // if (reader.TokenType==JsonTokenType.StartObject|| reader.TokenType==JsonTokenType.StartArray)
                        // {
                        //     JsonSerializer.
                        // }
                        if (CustomScenarioGloble.JsonConverters.ContainsKey(type))
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

                        break;
                    }
                }
            }
        }
      

        return null;
    }

    public override void Write(Utf8JsonWriter writer, CustomScenarioValue value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer,value); 
    }
}