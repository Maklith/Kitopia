using PluginCore;

namespace Core.SDKs.Services.Plugin;

public class PluginOverall
{
    public static readonly Dictionary<string, List<Func<string, SearchViewItem?>>> SearchActions = new();
    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();

    public static List<ScreenCaptureExMethod> AllScreenCaptureExMethods =>
        ScreenCaptureExMethods.Values.SelectMany(e => e).ToList();
}