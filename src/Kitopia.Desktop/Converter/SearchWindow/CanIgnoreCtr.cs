#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PluginCore;

#endregion

namespace Kitopia.Desktop.Converter.SearchWindow;

public class CanIgnoreCtr : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null) return false;

        switch ((FileType)value)
        {
            case FileType.应用程序: return true;
            case FileType.UWP应用: return true;
            case FileType.Word文档: return true;
            case FileType.PPT文档: return true;
            case FileType.Excel文档: return true;
            case FileType.PDF文档: return true;
            case FileType.图像: return true;
            case FileType.文件: return true;
            default: return false;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}