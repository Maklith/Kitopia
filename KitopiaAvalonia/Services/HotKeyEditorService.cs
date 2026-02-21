using Core.Services.HotKey;
using Core.Services.Interfaces;
using KitopiaAvalonia.Windows;
using Window = Avalonia.Controls.Window;
using WindowStartupLocation = Avalonia.Controls.WindowStartupLocation;

namespace KitopiaAvalonia.Services;

public class HotKeyEditorService : IHotKeyEditor
{
    public void EditByUuid(string uuid, object? owner)
    {
        var hotKeyModel = HotKeyManager.HotKetImpl.GetByUuid(uuid);
        if (hotKeyModel == null) return;

        var hotKeyEditor = new HotKeyEditorWindow(hotKeyModel.Value);

        hotKeyEditor.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        hotKeyEditor.Title = "修改快捷键";
        if (owner is null)
            hotKeyEditor.Show();
        else
            hotKeyEditor.ShowDialog((Window)owner);
    }
}