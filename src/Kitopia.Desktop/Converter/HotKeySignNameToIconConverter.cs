using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter;

public class HotKeySignNameToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return "\uf4b9";

        if (s.StartsWith("Kitopia情景"))
        {
            return "\ue065";
        }

        if (s.StartsWith("Kitopia_"))
        {
            s = s.Substring("Kitopia_".Length);
        }

        return s switch
        {
            "置顶窗口快捷键" or "置顶当前窗口" => "\uf602",
            "显示搜索框" or "唤出快速搜索" => "\uf68f",
            "文件速览" => "\uf3ae", // ic_fluent_document_search_20_regular
            "截图" or "屏幕区域截图" => "\uf68d", // ic_fluent_screenshot_20_regular
            "激活鼠标快捷菜单" or "鼠标快捷菜单" => "\uf37a",
            _ => "\uf4b9"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
