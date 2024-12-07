using PluginCore;

namespace Core.SDKs.Services;

public interface IScreenCaptureWindow
{
    public void CaptureScreen(Stack<ScreenCaptureResult> results);

    public Task<ScreenCaptureInfo> GetScreenCaptureInfo();
}