using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Kitopia.Desktop.Converter;

public class HotKeySignNameToDescriptionCtr : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var s = value as string;
        if (string.IsNullOrWhiteSpace(s)) return "全局交互热键";

        if (s.StartsWith("Kitopia情景"))
        {
            return "自定义自动化工作流";
        }

        if (s.StartsWith("Kitopia_"))
        {
            s = s.Substring("Kitopia_".Length);
        }

        return s switch
        {
            "置顶窗口快捷键" or "置顶当前窗口" => "固定当前窗口于最顶层显示",
            "显示搜索框" or "唤出快速搜索" => "快速检索应用、文件与核心功能",
            "文件速览" => "空格快速预览选中文件内容",
            "截图" or "屏幕区域截图" => "区域截取、长截图与图像标注",
            "激活鼠标快捷菜单" or "鼠标快捷菜单" => "呼出鼠标悬浮快捷工具面板",
            _ => "全局交互热键"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
