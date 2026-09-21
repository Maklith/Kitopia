using System.Text.Json;
using Avalonia;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Windows;
using PluginCore;

namespace KitopiaTest;

[TestClass]
public sealed class HotKeyProcessScopeTests
{
    [TestMethod]
    [DataRow(HotKeyProcessScope.All, "notepad", true)]
    [DataRow(HotKeyProcessScope.Include, "Explorer", true)]
    [DataRow(HotKeyProcessScope.Include, "EXPLORER.EXE", true)]
    [DataRow(HotKeyProcessScope.Include, "notepad", false)]
    [DataRow(HotKeyProcessScope.Exclude, "explorer", false)]
    [DataRow(HotKeyProcessScope.Exclude, "notepad", true)]
    [DataRow(HotKeyProcessScope.Include, null, false)]
    [DataRow(HotKeyProcessScope.Exclude, null, false)]
    public void CanExecuteInProcess_AppliesForegroundProcessRule(HotKeyProcessScope scope, string? process, bool expected)
    {
        var model = new HotKeyModel { ProcessScope = scope, ProcessNames = [" C:\\Windows\\explorer.exe "] };
        Assert.AreEqual(expected, model.CanExecuteInProcess(process));
    }

    [TestMethod]
    public void Serialization_PreservesScopeAndDisabledState()
    {
        var model = new HotKeyModel
        {
            IsEnabled = false, SelectKey = EKey.空格, ProcessScope = HotKeyProcessScope.Exclude,
            ProcessNames = ["explorer.exe", "code"], IgnoreTextInput = true
        };
        var json = JsonSerializer.Serialize(model, ConfigManger.DefaultOptions);
        var restored = JsonSerializer.Deserialize<HotKeyModel>(json, ConfigManger.DefaultOptions)!;
        Assert.AreEqual(model.UUID, restored.UUID);
        Assert.IsFalse(restored.IsEnabled);
        Assert.AreEqual(model.ProcessScope, restored.ProcessScope);
        CollectionAssert.AreEqual(model.ProcessNames, restored.ProcessNames);
        Assert.IsTrue(restored.IgnoreTextInput);
        Assert.IsFalse(json.Contains("ProcessScopeDescription"));
    }

    [TestMethod]
    public void Serialization_OldShortcut_DefaultsToAllProcesses()
    {
        var restored = JsonSerializer.Deserialize<HotKeyModel>("{\"SelectKey\":65,\"IsEnabled\":true}")!;
        Assert.AreEqual(HotKeyProcessScope.All, restored.ProcessScope);
        Assert.IsTrue(restored.CanExecuteInProcess("any-process"));
    }

    [TestMethod]
    public void Config_PreviewUsesNormalScopedHotkey()
    {
        var model = new KitopiaConfig().mouseHotkey;
        Assert.AreEqual(HotKeyType.Keyboard, model.Type);
        Assert.AreEqual(EKey.空格, model.SelectKey);
        Assert.IsTrue(model.CanExecuteInProcess("explorer"));
        Assert.IsFalse(model.CanExecuteInProcess("notepad"));
        Assert.IsTrue(model.IgnoreTextInput);
    }

    [TestMethod]
    [DataRow(100, 150, false)]
    [DataRow(100, 900, true)]
    [DataRow(1750, 400, false)]
    public void Placement_UsesAvailableSideAndStaysOnScreen(int x, int y, bool above)
    {
        var anchor = new PixelRect(x, y, 100, 40);
        var area = new PixelRect(0, 0, 1920, 1080);
        var method = typeof(MouseQuickWindow).GetMethod("GetPreviewBounds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var result = (PixelRect)method.Invoke(null, [anchor, area, 1d, new Size(960, 680)])!;
        Assert.IsTrue(result.X >= area.X && result.Right <= area.Right && result.Y >= area.Y && result.Bottom <= area.Bottom);
        Assert.IsTrue(above ? result.Bottom < anchor.Y : result.Y > anchor.Bottom);
    }
}
