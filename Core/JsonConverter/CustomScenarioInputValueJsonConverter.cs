using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Core.SDKs.CustomScenario;

namespace Core.JsonConverter;

public class CustomScenarioInputValueJsonConverter : JsonConverter<CustomScenarioInputValue>
{
    public override CustomScenarioInputValue? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        object value;
        System.Type type=null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.GetString() == "Type")
                {
                    reader.Read();
                    
                  type= new TypeJsonConverter().Read(ref reader, typeToConvert, options);
                }
                if (reader.GetString() == "Value")
                {
                    reader.Read();
                    if (reader.TokenType==JsonTokenType.Null)
                    {
                        reader.Read();
                        return new CustomScenarioInputValue
                        {
                            Type = type
                        };
                    }
                    if (CustomScenarioGloble.JsonConverters.ContainsKey(type))
                    {
                        var jsonConverter = CustomScenarioGloble.JsonConverters[type];
                        // 获取 Read 方法
                        var readerValueSpan = reader.ValueSpan;
                        reader.Read();
                        return new CustomScenarioInputValue
                        {
                            Type = type,
                            Value = jsonConverter.Deserialize(readerValueSpan)
                        };
                    }
                }
            }
        }
      

        return null;
    }

    public override void Write(Utf8JsonWriter writer, CustomScenarioInputValue value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer,value); 
    }
}