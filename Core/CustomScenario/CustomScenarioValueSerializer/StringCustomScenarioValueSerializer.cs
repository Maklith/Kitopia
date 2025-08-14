using System.Text;
using PluginCore;

namespace Core.SDKs.CustomScenario.CustomScenarioValueSerializer;

public class StringCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is null) return "";
        return value.ToString();
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return Encoding.UTF8.GetString(value);
    }
}