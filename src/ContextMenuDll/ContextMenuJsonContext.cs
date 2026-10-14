using System.Text.Json.Serialization;

namespace ContextMenuDll;

[JsonSerializable(typeof(ContextMenuConfig))]
[JsonSerializable(typeof(ContextMenuSettings))]
public partial class ContextMenuJsonContext : JsonSerializerContext
{
}
