using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario.CustomScenarioValueSerializer;

public class BoolCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        if (value is null) return false.ToString();

        return value.ToString();
    }

    public object? Deserialize(string? value)
    {
        return bool.Parse(value);
    }
}