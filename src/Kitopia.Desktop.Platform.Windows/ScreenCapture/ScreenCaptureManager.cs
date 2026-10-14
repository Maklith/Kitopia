using Kitopia.Desktop.Features.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Platform.Windows.ScreenCapture;

public class ScreenCaptureManager : IScreenCaptureManager
{
    public void SetCaptureMethodName(string methodName)
    {
        ConfigManger.Config.截图方法 = methodName;
    }

    public List<string> GetCaptureMethodName()
    {
        return ["自动", "WGC"];
    }

    public List<ScreenCaptureInfo> GetAllScreenInfo()
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWgc));
                if (firstOrDefault is null)
                {
                    ConfigManger.Config.截图方法 = "自动";
                    return new List<ScreenCaptureInfo>();
                }

                return firstOrDefault.GetAllScreenInfo();
            }
            default:
            {
                foreach (var screenCapture in screenCaptures)
                {
                    var screenCaptureInfos = screenCapture.GetAllScreenInfo();
                    if (screenCaptureInfos.Count > 0) return screenCaptureInfos;
                }

                break;
            }
        }

        return [];
    }
    public List<WindowInfo> GetAllWindowInfo()
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWgc));
                if (firstOrDefault is null)
                {
                    ConfigManger.Config.截图方法 = "自动";
                    return new List<WindowInfo>();
                }

                return firstOrDefault.GetAllWindowInfo();
            }
            default:
            {
                foreach (var screenCapture in screenCaptures)
                {
                    var screenCaptureInfos = screenCapture.GetAllWindowInfo();
                    if (screenCaptureInfos.Count > 0) return screenCaptureInfos;
                }

                break;
            }
        }

        return [];
    }

    public ScreenCaptureInfo GetScreenCaptureInfoByIndex(int index)
    {
        return default;
    }

   

    public Stack<ScreenCaptureResult> CaptureAllScreenBytes()
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {
            

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWgc));
                if (firstOrDefault is null)
                {
                    ConfigManger.Config.截图方法 = "自动";
                    return new Stack<ScreenCaptureResult>();
                }

                return firstOrDefault.CaptureAllScreenMat();
            }
            default:
            {
                foreach (var screenCapture in screenCaptures)
                {
                    var screenCaptureInfos = screenCapture.CaptureAllScreenMat();
                    if (screenCaptureInfos.Count > 0) return screenCaptureInfos;
                }

                break;
            }
        }

        return [];
    }
    

    public ScreenCaptureResult CaptureScreenBytes(ScreenCaptureInfo screenCaptureInfo)
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {
          

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWgc));
                if (firstOrDefault is null)
                {
                    ConfigManger.Config.截图方法 = "自动";
                    return new ScreenCaptureResult();
                }

                return firstOrDefault.CaptureScreenMat(screenCaptureInfo);
            }
            default:
            {
                foreach (var screenCapture in screenCaptures)
                {
                    var screenCaptureInfos = screenCapture.CaptureScreenMat(screenCaptureInfo);
                    return screenCaptureInfos;
                }

                break;
            }
        }
        return default;
    }
}