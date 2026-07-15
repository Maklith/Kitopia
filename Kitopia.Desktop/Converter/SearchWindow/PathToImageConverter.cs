#region

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Search;
using Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using CustomScenarioModel = Kitopia.Desktop.Features.CustomScenario.CustomScenario;

#endregion

namespace Kitopia.Desktop.Converter.SearchWindow;

public partial class PathToImageConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        //Console.WriteLine("开始获取  "+DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond );
        var icon = values.Count > 0 ? values[0] : null;
        if (icon is not null) return icon;

        var dataContext = values.Count > 1 ? values[1] : null;
        if (dataContext is SearchViewItem searchViewItem)
        {
            if (searchViewItem is { Icon: null })
            {
                ServiceManager.Services.GetService<IAppToolService>().LoadIcon(searchViewItem);
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
                ServiceManager.Services.GetService<IAppToolService>().LoadIcon(customScenario);
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

        if (dataContext is CustomScenarioModel customScenario1)
        {
            if (customScenario1 is { Icon: null })
            {
                ServiceManager.Services.GetService<IAppToolService>().LoadIcon(customScenario1);
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
