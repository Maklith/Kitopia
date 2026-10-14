using Kitopia.Feature.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter;

public class HotKeySignNameToDescriptionCtr : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = values.Count > 0 ? values[0] as string : null;
        if (string.IsNullOrWhiteSpace(s)) return Lang.Get("lang.kitopia.global_hotkey");

        if (s.StartsWith("Kitopia情景"))
        {
            return Lang.Get("lang.kitopia.custom_automation_workflow");
        }

        if (s.StartsWith("Kitopia_"))
        {
            s = s.Substring("Kitopia_".Length);
        }

        return s switch
        {
            "置顶窗口快捷键" or "置顶当前窗口" => Lang.Get("lang.kitopia.keep_the_current_window_on_top"),
            "显示搜索框" or "唤出快速搜索" => Lang.Get("lang.kitopia.quickly_find_apps_files_and_actions"),
            "文件速览" => Lang.Get("lang.kitopia.press_space_to_preview_selected_files"),
            "截图" or "屏幕区域截图" => Lang.Get("lang.kitopia.screen_capture_scrolling_screenshots_and_annotations"),
            "激活鼠标快捷菜单" or "鼠标快捷菜单" => Lang.Get("lang.kitopia.open_the_mouse_quick_tools_panel"),
            _ => Lang.Get("lang.kitopia.global_hotkey")
        };
    }

}
