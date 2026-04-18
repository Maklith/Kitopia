using PluginCore.CustomScenario;

namespace Core.CustomScenario.CustomScenarioValueSerializer;

public class DoubleCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is null) return 0.ToString();
        return value.ToString();
    }

    public object? Deserialize(string? value)
    {
        return double.Parse(value);
    }
}