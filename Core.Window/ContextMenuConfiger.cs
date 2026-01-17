// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core.Window
// FileName:ContextMenuConfiger.cs
// Date: 2026/01/17 21:01
// FileEffect:

using System.Text.Json;
using Core.Services.Interfaces;

namespace Core.Window;

public class ContextMenuConfiger : IContextMenuConfiger
{
    private const string ConfigFileName = "KitopiaContextMenu.json";
    private readonly string _configPath;

    public ContextMenuConfiger()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var configDir = Path.Combine(baseDir, "configs");
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
        _configPath = Path.Combine(configDir, ConfigFileName);
    }

    private class ContextMenuConfigModel
    {
        public List<ContextMenuItem> Items { get; set; } = new();
    }

    private ContextMenuConfigModel LoadConfig()
    {
        if (!File.Exists(_configPath))
        {
            return new ContextMenuConfigModel();
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<ContextMenuConfigModel>(json) ?? new ContextMenuConfigModel();
        }
        catch
        {
            return new ContextMenuConfigModel();
        }
    }

    private bool SaveConfig(ContextMenuConfigModel config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_configPath, json);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool OverwriteMenuItems(List<ContextMenuItem> contextMenuItems)
    {
        var config = new ContextMenuConfigModel { Items = contextMenuItems };
        return SaveConfig(config);
    }

    public bool AddMenuItem(ContextMenuItem contextMenuItem)
    {
        var config = LoadConfig();
        config.Items.Add(contextMenuItem);
        return SaveConfig(config);
    }

    public bool RemoveMenuItem(string title)
    {
        var config = LoadConfig();
        var itemToRemove = config.Items.FirstOrDefault(x => x.Title == title);
        if (itemToRemove != null)
        {
            config.Items.Remove(itemToRemove);
            return SaveConfig(config);
        }
        return false;
    }

    public bool RemoveMenuItem(ContextMenuItem contextMenuItem)
    {
        var config = LoadConfig();
        // Match by properties since we deserialize fresh objects
        var itemToRemove = config.Items.FirstOrDefault(x => 
            x.Title == contextMenuItem.Title && 
            x.Command == contextMenuItem.Command && 
            x.Arguments == contextMenuItem.Arguments);
            
        if (itemToRemove != null)
        {
            config.Items.Remove(itemToRemove);
            return SaveConfig(config);
        }
        return false;
    }

    public List<ContextMenuItem> GetAllMenuItems()
    {
        return LoadConfig().Items;
    }
}
