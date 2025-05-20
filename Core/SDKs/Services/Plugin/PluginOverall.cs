using Core.SearchWindow.InputData;
using Core.ViewModel;
using PluginCore;
using PluginCore.Onnx;

namespace Core.SDKs.Services.Plugin;

public class PluginOverall
{
    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();
    public static readonly Dictionary<string,List<OnnxModelInfoWrapper>> OnnxModelInfos = new();
    public static readonly Dictionary<string,Dictionary<string,Func<IInferenceSession>>> OnnxRuntimes = new();
    public static readonly Dictionary<string, List<Func<string, IEnumerable<InputData>>>> SearchWindowInputDataIdentifies = new();
    public static readonly Dictionary<string, List<Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>>> SearchWindowInputDataAnalyzers = new();
    
    public static List<OnnxModelInfoWrapper> AllOnnxModelInfos =>
        OnnxModelInfos.Values.SelectMany(e => e).ToList();
    public static List<string> AllTargetDevices => OnnxRuntimes.Values.SelectMany(e=>e.Keys).ToList();
    public static List<ScreenCaptureExMethod> AllScreenCaptureExMethods =>
        ScreenCaptureExMethods.Values.SelectMany(e => e).ToList();
    public static Func<IInferenceSession>? GetOnnxRuntime(string targetDevice)
    {
        var firstOrDefault = OnnxRuntimes.Values.SelectMany(e => e).FirstOrDefault(e => e.Key == targetDevice);
         
        return firstOrDefault.Value??null;
    }

    static PluginOverall()
    {
        SearchWindowInputDataIdentifies["Kitopia"] = new List<Func<string, IEnumerable<InputData>>>()
        {
            (s =>new PathIdentifier().IdentifyInputData(s) ),
            (s => new ImageIdentifier().IdentifyInputData(s)),
            (s => new MathIdentifier().IdentifyInputData(s)),
            (s => new KnowCommandIdentifier().IdentifyInputData(s)),
            (s => new UrlIdentifier().IdentifyInputData(s)),
        };
        SearchWindowInputDataAnalyzers["Kitopia"] = new List<Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>>()
        {
            (s => new PathAnalyzer().AnalyzeInputData(s)),
            (s => new ImageAnalyzer().AnalyzeInputData(s)),
            (s => new MathAnalyzer().AnalyzeInputData(s)),  
            (s => new KnowCommandAnalyzer().AnalyzeInputData(s)),
            (s => new UrlAnalyzer().AnalyzeInputData(s)),
        };
    }
        
}