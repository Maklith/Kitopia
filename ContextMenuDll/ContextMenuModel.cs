namespace ContextMenuDll;

public class ContextMenuConfig
{
    public List<ContextMenuItem> Items { get; set; } = new();
}

public class ContextMenuItem
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public List<ContextMenuItem> SubItems { get; set; } = new();
}
