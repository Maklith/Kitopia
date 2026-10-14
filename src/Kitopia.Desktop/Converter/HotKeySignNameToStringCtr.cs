using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Kitopia.Feature.Localization;
using Kitopia.Desktop.Features.CustomScenario;

namespace Kitopia.Desktop.Converter;

public class HotKeySignNameToStringCtr : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return FormatFriendlyName(values.FirstOrDefault() as string);
    }

    public static string FormatFriendlyName(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        if (s.StartsWith("Kitopia情景"))
        {
            var parts = s.Split('_');
            if (parts.Length > 1)
            {
                var uuid = parts[1];
                var firstOrDefault = CustomScenarioManger.CustomScenarios.FirstOrDefault(e => e.Uuid == uuid);
                if (firstOrDefault is not null) return firstOrDefault.Name;
            }
        }

        if (s.StartsWith("Kitopia_"))
        {
            s = s.Substring("Kitopia_".Length);
        }

        return s switch
        {
            "置顶窗口快捷键" => Lang.Get("lang.kitopia.pin_current_window"),
            "显示搜索框" => Lang.Get("lang.kitopia.open_quick_search"),
            "激活鼠标快捷菜单" => Lang.Get("lang.kitopia.mouse_quick_menu"),
            "截图" => Lang.Get("lang.kitopia.capture_screen_area"),
            "文件速览" => Lang.Get("lang.kitopia.file_preview"),
            "划词翻译" => Lang.Get("lang.kitopia.selection_translation"),
            "自动划词翻译" => Lang.Get("lang.kitopia.selection_translation_auto_hotkey"),
            _ => s
        };
    }

}
