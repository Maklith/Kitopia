using System.Text;

namespace Core.SDKs.CustomScenario.CustomScenarioValueSerializer;

public class Int32CustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is null) return 0.ToString();
        return value.ToString();
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return int.Parse(Encoding.UTF8.GetString(value));
    }
}