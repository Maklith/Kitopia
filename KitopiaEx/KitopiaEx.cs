using System;
using KitopiaEx.CustomScenarioValueSerializer;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using KitopiaEx.Ocr;
using Microsoft.Extensions.DependencyInjection;
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
        Kitopia._i18n.TryAdd(typeof(System.Collections.Generic.IEnumerable<OcrResult>).FullName, "文字识别结果数组");
        ServiceProvider = serviceProvider;
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureInfo), info =>
        {
            var screenCaptureInfo = (ScreenCaptureInfo)info;
            return
                $"显示器:{screenCaptureInfo.ScreenInfo.hdcMonitor},起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}";
        });
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureResult), e =>
        {
            var screenCaptureResult = (ScreenCaptureResult)e;
            var screenCaptureInfo = screenCaptureResult.Info;
            return
                $"显示器:{screenCaptureInfo.ScreenInfo.hdcMonitor},起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}\nByte数据:{(screenCaptureResult.Bytes is null?"不存在":"存在")}\nBitmap数据:{(screenCaptureResult.Source is null?"不存在":"存在")}";
        });
        Kitopia.JsonConverters.TryAdd(typeof(ScreenCaptureInfo), new ScreenCaptureInfoCustomScenarioValueSerializer());
    }

    public void OnDisabled()
    {
        Kitopia.JsonConverters.Remove(typeof(ScreenCaptureInfo));
    }

    public static IServiceProvider GetServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<KitopiaEx>();
        services.AddSingleton<ImageTools>();
        services.AddSingleton<SearchItemEx>();
        services.AddSingleton<ClipboardEx>();
        services.AddSingleton<ImageTools>();
        services.AddTransient<ScreenCaptureInfoSelfConnector>();
        services.AddSingleton<KeyboardSimulation>();
        services.AddSingleton<ScreenCaptureNode>();
        services.AddTransient<Ocr.Ocr>();
        return services.BuildServiceProvider();
    }
}