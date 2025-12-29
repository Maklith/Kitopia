using System;
using System.Collections.Generic;
using KitopiaEx.CustomScenarioMethods;
using KitopiaEx.CustomScenarioValueSerializer;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using KitopiaEx.Ocr;
using KitopiaEx.QRCode;
using KitopiaEx.Translate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PluginCore;

namespace KitopiaEx;

public class KitopiaEx : IPlugin
{
    public static IServiceProvider ServiceProvider;

    private IPlugin _pluginImplementation;

    public void OnEnabled(IServiceProvider serviceProvider)
    {
        //MessageBox.Show("OnEnabled");
        Kitopia._i18n.TryAdd("System.Windows.Media.Imaging.BitmapSource", "图像BitmapSource");
        Kitopia._i18n.TryAdd(typeof(ScreenCaptureInfoSelfConnector).FullName, "屏幕截图信息");
        Kitopia._i18n.TryAdd(typeof(ScreenCaptureInfo).FullName, "屏幕截图信息");
        Kitopia._i18n.TryAdd(typeof(ScreenCaptureResult).FullName, "屏幕截图数据");
        Kitopia._i18n.TryAdd(typeof(IEnumerable<OcrResult>).FullName, "文字识别结果数组");
        ServiceProvider = serviceProvider;
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureInfo), info =>
        {
            var screenCaptureInfo = (ScreenCaptureInfo)info;
            if (screenCaptureInfo.ScreenCaptureType == ScreenCaptureType.屏幕)
            {
                return
                    $"显示器:{screenCaptureInfo.ScreenInfo.hMonitor},起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}";
            }
            if (screenCaptureInfo.ScreenCaptureType == ScreenCaptureType.窗口)
            {
                return
                    $"窗口:{screenCaptureInfo.WindowInfo.Title}";
            }
            return
                $"显示器:{screenCaptureInfo.ScreenInfo.hMonitor},起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}";
        });
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureResult), e =>
        {
            var screenCaptureResult = (ScreenCaptureResult)e;
            var screenCaptureInfo = screenCaptureResult.Info;
            if (screenCaptureInfo.ScreenCaptureType == ScreenCaptureType.屏幕)
            {
                return
                    $"显示器:{screenCaptureInfo.ScreenInfo.hMonitor},起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}";
            }
            if (screenCaptureInfo.ScreenCaptureType == ScreenCaptureType.窗口)
            {
                return
                    $"窗口:{screenCaptureInfo.WindowInfo.Title}";
            }
            return
                $"起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}\nBitmap数据:{(screenCaptureResult.Source is null?"不存在":"存在")}";
        });
        Kitopia.JsonConverters.TryAdd(typeof(ScreenCaptureInfo), new ScreenCaptureInfoCustomScenarioValueSerializer());
    }

    public void OnDisabled()
    {
        Kitopia.JsonConverters.Remove(typeof(ScreenCaptureInfo));
        Kitopia.ToolTipConverters.Remove(typeof(ScreenCaptureInfo));
        Kitopia.ToolTipConverters.Remove(typeof(ScreenCaptureResult));
    }

    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<KitopiaEx>();
        services.AddSingleton<SearchItemScenarioMethod>();
        services.AddTransient<ScreenCaptureInfoSelfConnector>();
        services.AddSingleton<KeyboardSimulation>();
        services.AddSingleton<ScreenCaptureNode>();
        services.AddTransient<Ocr.Ocr>();
        services.AddTransient<ImagePin.ImagePin>();
        services.AddTransient<Translate.Translate>();
        services.AddTransient<TranslateInputDataAnalyzer>();
        services.AddTransient<QrCoder>();
        services.AddTransient<Image>();
        
        services.AddTransient<ScreenCaptureExs.Ocr>();
        services.AddTransient<ScreenCaptureExs.ImagePin>();
        services.AddTransient<ScreenCaptureExs.QRCode>();
        services.AddTransient<ScreenCaptureExs.Translate>();
        services.AddTransient<ScreenCaptureExs.SaveImageToFile>();
        
        services.AddTransient<ImagePinScenarioMethod>();
        
        var buildServiceProvider = services.BuildServiceProvider();
        ServiceProvider = buildServiceProvider;
        return buildServiceProvider;
    }
}