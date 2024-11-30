using Core.SDKs.HotKey;
using Core.SDKs.Services;
using KitopiaAvalonia;
using KitopiaAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;
using Window = Avalonia.Controls.Window;
using WindowStartupLocation = Avalonia.Controls.WindowStartupLocation;

namespace Kitopia.Services;

public class HotKeyEditorService : IHotKeyEditor
{
    public void EditByUuid(string uuid, object? owner)
    {
        var hotKeyModel = HotKeyManager.HotKetImpl.GetByUuid(uuid);
        if (hotKeyModel == null)
        {
            return;
        }

        var hotKeyEditor = new HotKeyEditorWindow(hotKeyModel.Value);

        hotKeyEditor.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        hotKeyEditor.Title = "修改快捷键";
        if (owner is null)
        {
            hotKeyEditor.Show();
        }
        else
        {
            hotKeyEditor.ShowDialog((Window)owner);
        }
    }
}