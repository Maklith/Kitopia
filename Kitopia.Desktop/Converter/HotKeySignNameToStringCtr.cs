using System;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.CustomScenario;

namespace Kitopia.Desktop.Converter;

public class HotKeySignNameToStringCtr : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = (string)value!;
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
            "置顶窗口快捷键" => "置顶当前窗口",
            "显示搜索框" => "唤出快速搜索",
            "激活鼠标快捷菜单" => "鼠标快捷菜单",
            "截图" => "屏幕区域截图",
            _ => s
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}