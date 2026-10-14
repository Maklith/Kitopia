using Kitopia.Feature.Localization;
using Kitopia.Desktop.Features.Services.HotKey;
using PluginCore;

namespace Kitopia.Desktop.Features.Utils;

public static class HotKeyDisplay
{
    public static string MouseButtonName(ushort? button) => button switch
    {
        (ushort)MouseHookType.LeftButton => Lang.Get("lang.kitopia.left_mouse_button"),
        (ushort)MouseHookType.RightButton => Lang.Get("lang.kitopia.right_mouse_button"),
        (ushort)MouseHookType.MiddleButton => Lang.Get("lang.kitopia.middle_mouse_button"),
        (ushort)MouseHookType.XButton1 => Lang.Get("lang.kitopia.mouse_button_4"),
        (ushort)MouseHookType.XButton2 => Lang.Get("lang.kitopia.mouse_button_5"),
        null or 0 or ushort.MaxValue => Lang.Get("lang.kitopia.not_set"),
        _ => Lang.Format("lang.kitopia.hotkeys.mouse_button", button)
    };

    public static string ScopeDescription(HotKeyModel model)
    {
        var processes = string.Join(", ", model.ProcessNames);
        var scope = model.ProcessScope switch
        {
            HotKeyProcessScope.Include => Lang.Format("lang.kitopia.hotkeys.scope.include", processes),
            HotKeyProcessScope.Exclude => Lang.Format("lang.kitopia.hotkeys.scope.exclude", processes),
            _ => Lang.Get("lang.kitopia.all_processes")
        };
        if (model.Type == HotKeyType.Mouse)
            scope += model.MouseTrigger == MouseHotKeyTrigger.DragRelease
                ? Lang.Format("lang.kitopia.hotkeys.drag_distance", model.DragDistancePixels)
                : Lang.Format("lang.kitopia.hotkeys.hold_duration", model.PressTimeMillis);
        if (!model.IsEnabled) scope += Lang.Get("lang.kitopia.disabled_suffix");
        return scope;
    }
}
