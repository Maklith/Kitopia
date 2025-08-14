using System.Text;
using PluginCore;

namespace Core.CustomScenario.CustomScenarioValueSerializer;

public class DoubleCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is null) return 0.ToString();
        return value.ToString();
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return double.Parse(Encoding.UTF8.GetString(value));
    }
}