using System;
using KitopiaEx.CustomScenarioValueSerializer;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
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
        ServiceProvider = serviceProvider;
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureInfo), info =>
        {
            var screenCaptureInfo = (ScreenCaptureInfo)info;
            return $"显示器:{screenCaptureInfo.hdcMonitor},起始坐标:{screenCaptureInfo.X},{screenCaptureInfo.Y}\n大小:{screenCaptureInfo.Width}x{screenCaptureInfo.Height}";
        });
        Kitopia.JsonConverters.TryAdd(typeof(ScreenCaptureInfo),new ScreenCaptureInfoCustomScenarioValueSerializer());
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
        return services.BuildServiceProvider();
    }
}