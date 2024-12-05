
using System.Text;
using Core.SDKs.CustomScenario;

public class StringCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        return value.ToString();
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return Encoding.UTF8.GetString(value);    
        
    }
}