using Kitopia.Desktop.Features.Services.HotKey;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Windows;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Window = Avalonia.Controls.Window;
using WindowStartupLocation = Avalonia.Controls.WindowStartupLocation;

namespace Kitopia.Desktop.Services;

public class HotKeyEditorService : IHotKeyEditor
{
    public void EditByUuid(string uuid, object? owner)
    {
        var hotKeyModel = ServiceManager.Services.GetService<IHotKetImpl>()!.GetByUuid(uuid);
        if (hotKeyModel == null) return;

        var hotKeyEditor = new HotKeyEditorWindow(hotKeyModel) {
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "修改快捷键"
        };

        if (owner is null)
            hotKeyEditor.Show();
        else
            hotKeyEditor.ShowDialog((Window)owner);
    }
}