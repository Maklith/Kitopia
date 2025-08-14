#region

using System;
using System.Globalization;
using Avalonia.Data.Converters;
using PluginCore;

#endregion

namespace KitopiaAvalonia.Converter.SearchWindow;

public class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;

        switch (parameter)
        {
            case "RunAsAdmin":
            {
                switch ((FileType)value)
                {
                    case FileType.应用程序: return true;
                    case FileType.命令: return true;
                    case FileType.UWP应用: return true;
                    default: return false;
                }
            }
            case "Folder":
            {
                switch ((FileType)value)
                {
                    case FileType.应用程序: return true;
                    case FileType.Word文档: return true;
                    case FileType.PPT文档: return true;
                    case FileType.Excel文档: return true;
                    case FileType.PDF文档: return true;
                    case FileType.图像: return true;
                    case FileType.文件: return true;
                    default: return false;
                }
            }
            case "Console":
            {
                switch ((FileType)value)
                {
                    case FileType.应用程序: return true;
                    case FileType.Word文档: return true;
                    case FileType.PPT文档: return true;
                    case FileType.Excel文档: return true;
                    case FileType.PDF文档: return true;
                    case FileType.图像: return true;
                    case FileType.文件夹: return true;
                    case FileType.文件: return true;
                    default: return false;
                }
            }
            case "Star":
            {
                switch ((FileType)value)
                {
                    case FileType.应用程序:
                    case FileType.文件:
                    case FileType.文件夹: return true;
                    default: return false;
                }
            }
            case "Pin":
            {
                switch ((FileType)value)
                {
                    case FileType.剪贴板图像: return false;
                    case FileType.数学运算: return false;
                    default: return true;
                }
            }
            default: return false;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}