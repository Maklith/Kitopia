using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Core.Window;

public class ScreenCaptureManager : IScreenCaptureManager
{
    public void SetCaptureMethodName(string methodName)
    {
        ConfigManger.Config.截图方法 = methodName;
    }

    public List<string> GetCaptureMethodName()
    {
        return new List<string>
        {
            "自动", "Directx11", "WGC"
        };
    }

    public List<ScreenCaptureInfo> GetAllScreenInfo()
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWGC));
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

        return null;
    }
    public List<WindowInfo> GetAllWindowInfo()
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWGC));
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

        return null;
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
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWGC));
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

        return null;
    }
    

    public ScreenCaptureResult CaptureScreenBytes(ScreenCaptureInfo screenCaptureInfo)
    {
        var screenCaptures = ServiceManager.Services.GetServices<IScreenCapture>();
        switch (ConfigManger.Config.截图方法)
        {
          

            case "WGC":
            {
                var firstOrDefault = screenCaptures.FirstOrDefault(e => e.GetType() == typeof(ScreenCaptureByWGC));
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