
using Core.SDKs.CustomScenario;

public class StringCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        return null;
    }

    public object Deserialize(ReadOnlySpan<byte> value)
    {
        return null;
    }
}