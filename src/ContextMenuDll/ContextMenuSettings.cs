
namespace ContextMenuDll;

public class ContextMenuSettings
{
    public string ExternalConfigPath { get; set; } = string.Empty;
    public Dictionary<string, bool> Visibility { get; set; } = new();
}
