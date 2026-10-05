using Kitopia.Feature.Localization;
#region

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using PluginCore;

#endregion

namespace Kitopia.Desktop.Converter.SearchWindow;

public class SearchItemToInfo : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count == 0 || values[0] is not SearchViewItem item) return null;

        return item.FileType switch
        {
            FileType.文件夹 or FileType.应用程序 or FileType.Word文档 or FileType.PPT文档 or
                FileType.Excel文档 or FileType.PDF文档 or FileType.图像 or FileType.文件 =>
                Lang.Format("lang.kitopia.search.name_location", Path.GetFileName(item.OnlyKey), item.OnlyKey),
            FileType.命令 or FileType.URL =>
                Lang.Format("lang.kitopia.search.name_target", item.ItemDisplayName, item.OnlyKey),
            FileType.数学运算 =>
                Lang.Format("lang.kitopia.messages.copy_value_to_clipboard", item.ItemDisplayName.Remove(0, 1)),
            _ => null
        };
    }
}
