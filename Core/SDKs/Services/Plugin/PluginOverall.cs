using PluginCore;
using PluginCore.Onnx;

namespace Core.SDKs.Services.Plugin;

public class PluginOverall
{
    public static readonly Dictionary<string, List<Func<string, SearchViewItem?>>> SearchActions = new();
    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();
    public static readonly Dictionary<string,List<OnnxModelInfoWrapper>> OnnxModelInfos = new();
    public static readonly Dictionary<TargetDevice,Func<IInferenceSession>> OnnxRuntimes = new();
    public static List<OnnxModelInfoWrapper> AllOnnxModelInfos =>
        OnnxModelInfos.Values.SelectMany(e => e).ToList();
    public static List<TargetDevice> AllTargetDevices => OnnxRuntimes.Keys.ToList();
    public static List<ScreenCaptureExMethod> AllScreenCaptureExMethods =>
        ScreenCaptureExMethods.Values.SelectMany(e => e).ToList();
    
        
}