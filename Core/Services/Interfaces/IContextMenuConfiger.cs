// Author: liaom
// SolutionName: Kitopia
// ProjectName: Core
// FileName:IContextMenuConfiger.cs
// Date: 2026/01/17 21:01
// FileEffect:

namespace Core.Services.Interfaces;

public interface IContextMenuConfiger
{
    public bool OverwriteMenuItems(List<ContextMenuItem> contextMenuItems);
    public bool AddMenuItem(ContextMenuItem contextMenuItem);
    public bool RemoveMenuItem(string title);
    public bool RemoveMenuItem(ContextMenuItem contextMenuItem);
    public List<ContextMenuItem> GetAllMenuItems();
    
}