#region

using System;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Data.Converters;
using PluginCore;

#endregion

namespace KitopiaAvalonia.Converter.SearchWindow;

public class SearchItemToInfo : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not SearchViewItem) return null;

        var item = (SearchViewItem)value;
        var info = new StringBuilder();
        switch (item.FileType)
        {
            case FileType.文件夹:
                info.Append("名称:");
                info.AppendLine(item.OnlyKey.Substring(item.OnlyKey.LastIndexOf(Path.DirectorySeparatorChar) + 1,
                    item.OnlyKey.Length - item.OnlyKey.LastIndexOf(Path.DirectorySeparatorChar) - 1));
                info.Append("位置:");
                info.Append(item.OnlyKey);
                break;
            case FileType.命令:
            case FileType.URL:
                info.Append("名称:");
                info.AppendLine(item.ItemDisplayName);
                info.Append("目标:");
                info.Append(item.OnlyKey);
                break;
            case FileType.数学运算:
                info.Append($"点击将{item.ItemDisplayName.Remove(0, 1)}复制到剪贴板");
                break;
            case FileType.剪贴板图像:
            case FileType.None:
                break;
            case FileType.应用程序:
            case FileType.Word文档:
            case FileType.PPT文档:
            case FileType.Excel文档:
            case FileType.PDF文档:
            case FileType.图像:
            case FileType.文件:
            {
                info.Append("名称:");
                info.AppendLine(item.OnlyKey.Substring(item.OnlyKey.LastIndexOf(Path.DirectorySeparatorChar) + 1,
                    item.OnlyKey.Length - item.OnlyKey.LastIndexOf(Path.DirectorySeparatorChar) - 1));
                info.Append("位置:");
                info.Append(item.OnlyKey);
                break;
            }
            default:
                return null;
        }

        if (info.Length > 1) return info.ToString();

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}