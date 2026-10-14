using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Features.CustomScenario.CustomScenarioValueSerializer;

public class NodeConnectorClassCustomScenarioValueSerializer : ICustomScenarioValueSerializer
{
    public string Serialize<T>(T value)
    {
        return "";
    }

    public object? Deserialize(string? value)
    {
        return null;
    }
}