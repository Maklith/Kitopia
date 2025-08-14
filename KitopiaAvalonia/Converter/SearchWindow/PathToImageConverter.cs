#region

using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Core.CustomScenario;
using Core.Services;
using Core.ViewModel.TaskEditor;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

#endregion

namespace KitopiaAvalonia.Converter.SearchWindow;

public partial class PathToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        //Console.WriteLine("开始获取  "+DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond );
        if (value is not null) return value;

        var dataContext = ((Control)((CompiledBindingExtension)parameter).DefaultAnchor.Target).DataContext;
        if (dataContext is SearchViewItem searchViewItem)
        {
            if (searchViewItem is { Icon: null })
            {
                ServiceManager.Services.GetService<IAppToolService>().GetIconByItem(searchViewItem);
                return null;
                //.WriteLine("完成获取2 "+DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond );
            }

            switch (searchViewItem.FileType)
            {
                case FileType.命令:

                case FileType.便签:
                case FileType.数学运算:
                case FileType.剪贴板图像:
                case FileType.None:
                    return null;
                case FileType.文件夹:
                case FileType.自定义:
                case FileType.UWP应用:
                case FileType.应用程序:
                case FileType.Word文档:
                case FileType.PPT文档:
                case FileType.Excel文档:
                case FileType.PDF文档:
                case FileType.图像:
                case FileType.文件:
                case FileType.URL:
                case FileType.自定义情景:
                default:
                    break;
            }

            try
            {
                if (searchViewItem != null) return searchViewItem.Icon;
            }
            catch (Exception e)
            {
                Console.WriteLine(1);
                return null;
            }
        }

        if (dataContext is TaskEditorViewModel taskEditorViewModel)
        {
            var customScenario = taskEditorViewModel.Scenario;
            if (customScenario is { Icon: null })
            {
                ServiceManager.Services.GetService<IAppToolService>().GetIconByItem(customScenario);
                return null;
                //.WriteLine("完成获取2 "+DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond );
            }

            try
            {
                if (customScenario != null) return customScenario.Icon;
            }
            catch (Exception e)
            {
                Console.WriteLine(1);
                return null;
            }
        }

        if (dataContext is CustomScenario customScenario1)
        {
            if (customScenario1 is { Icon: null })
            {
                ServiceManager.Services.GetService<IAppToolService>().GetIconByItem(customScenario1);
                return null;
                //.WriteLine("完成获取2 "+DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond );
            }

            try
            {
                if (customScenario1 != null) return customScenario1.Icon;
            }
            catch (Exception e)
            {
                Console.WriteLine(1);
                return null;
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}