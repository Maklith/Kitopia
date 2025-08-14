using PluginCore;

namespace Core.SDKs.CustomScenario.CustomScenarioValueSerializer;

public class NodeConnectorClassCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        return "";
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return null;
    }
}