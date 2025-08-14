using System.Text;
using PluginCore;

namespace Core.CustomScenario.CustomScenarioValueSerializer;

public class BoolCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is null) return false.ToString();

        return value.ToString();
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return bool.Parse(Encoding.UTF8.GetString(value));
    }
}