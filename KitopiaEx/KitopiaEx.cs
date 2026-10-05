using PluginCore.Localization;
using System;
using System.Collections.Generic;
using KitopiaEx.CustomScenarioMethods;
using KitopiaEx.CustomScenarioValueSerializer;
using KitopiaEx.INodeInputConnector.ScreenCaptureInfoSelfConnector;
using KitopiaEx.Ocr;
using KitopiaEx.Translate;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace KitopiaEx;

public class KitopiaEx : IPlugin
{
    public static IServiceProvider ServiceProvider;

    private IPlugin _pluginImplementation;

    
    public void OnEnabled(IServiceProvider serviceProvider, Dictionary<string, IServiceProvider> dependencyServiceProviders)
    {
        //MessageBox.Show("OnEnabled");
        Kitopia.TypeNames.TryAdd("System.Windows.Media.Imaging.BitmapSource", "lang.kitopiaex.bitmapsource_image");
        Kitopia.TypeNames.TryAdd(typeof(ScreenCaptureInfoSelfConnector).FullName!, "lang.kitopiaex.screen_capture_information");
        Kitopia.TypeNames.TryAdd(typeof(ScreenCaptureInfo).FullName!, "lang.kitopiaex.screen_capture_information");
        Kitopia.TypeNames.TryAdd(typeof(ScreenCaptureResult).FullName!, "lang.kitopiaex.screen_capture_data");
        Kitopia.TypeNames.TryAdd(typeof(IEnumerable<OcrResult>).FullName!, "lang.kitopiaex.ocr_result_array");
        Kitopia.TypeNames.TryAdd(typeof(ImagePin.ImagePin).FullName!, "lang.kitopiaex.pinned_image_window");
        Kitopia.TypeNames.TryAdd(typeof(OcrResultShowWindow).FullName!, "lang.kitopiaex.ocr_result_window");
        ServiceProvider = serviceProvider;
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureInfo), info =>
        {
            var screenCaptureInfo = (ScreenCaptureInfo)info;
            if (screenCaptureInfo is { ScreenCaptureType: ScreenCaptureType.屏幕, ScreenInfo: not null, RequestRect: not null })
                return
                    Lang.Format("lang.kitopiaex.messages.screen_origin_value_value_size_value_x_value", screenCaptureInfo.RequestRect.Value.X, screenCaptureInfo.RequestRect.Value.Y, screenCaptureInfo.RequestRect.Value.Width, screenCaptureInfo.RequestRect.Value.Height);
            if (screenCaptureInfo.ScreenCaptureType == ScreenCaptureType.窗口)
            {
                return
                    Lang.Format("lang.kitopiaex.messages.window_value", screenCaptureInfo.WindowInfo?.Title);
            }
            return
                Lang.Get("lang.kitopiaex.messages.the_capture_information_may_be_invalid");
        });
        Kitopia.ToolTipConverters.TryAdd(typeof(ScreenCaptureResult), e =>
        {
            var screenCaptureResult = (ScreenCaptureResult)e;
            var screenCaptureInfo = screenCaptureResult.Info;
            if (screenCaptureInfo is { ScreenCaptureType: ScreenCaptureType.屏幕, ScreenInfo: not null, RequestRect: not null })
                return
                    Lang.Format("lang.kitopiaex.messages.screen_origin_value_value_size_value_x_value", screenCaptureInfo.RequestRect.Value.X, screenCaptureInfo.RequestRect.Value.Y, screenCaptureInfo.RequestRect.Value.Width, screenCaptureInfo.RequestRect.Value.Height);
            if (screenCaptureInfo.ScreenCaptureType == ScreenCaptureType.窗口)
            {
                return
                    Lang.Format("lang.kitopiaex.messages.window_value", screenCaptureInfo.WindowInfo?.Title);
            }
            return
                Lang.Get("lang.kitopiaex.messages.the_capture_information_may_be_invalid");
            
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
        services.AddTransient<CustomScenarioMethods.Ocr>();
        services.AddTransient<ImagePin.ImagePin>();
        services.AddTransient<CustomScenarioMethods.Translate>();
        services.AddTransient<TranslateInputDataAnalyzer>();
        services.AddTransient<QrCoder>();
        services.AddTransient<Image>();
        
        services.AddTransient<ScreenCaptureExs.ImagePin>();
        services.AddTransient<ScreenCaptureExs.QRCode>();
        services.AddTransient<ScreenCaptureExs.Translate>();
        services.AddTransient<ScreenCaptureExs.SaveImageToFile>();

        services.AddTransient<SearchWindow.InputDataAnalyzer.ImageAnalyzer>();
        services.AddTransient<SearchWindow.InputDataAnalyzer.SetTopmostWindowAnalyzer>();
        services.AddTransient<SearchWindow.InputDataAnalyzer.WindowSwitcherAnalyzer>();
        
        services.AddTransient<ImagePinScenarioMethod>();
        
        var buildServiceProvider = services.BuildServiceProvider();
        ServiceProvider = buildServiceProvider;
        return buildServiceProvider;
    }
}
