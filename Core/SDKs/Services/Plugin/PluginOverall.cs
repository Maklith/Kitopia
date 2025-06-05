using Core.SearchWindow.InputData;
using Core.ViewModel;
using PluginCore;
using PluginCore.Onnx;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SDKs.Services.Plugin;

public class PluginOverall
{
    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();
    public static readonly Dictionary<string,List<OnnxModelInfoWrapper>> OnnxModelInfos = new();
    public static readonly Dictionary<string,Dictionary<string,Func<IInferenceSession>>> OnnxRuntimes = new();
    public static readonly Dictionary<string, List<Func<string, IEnumerable<InputData>>>> SearchWindowInputDataIdentifies = new();
    public static readonly Dictionary<string,  List<(Func<IInputDataAnalyzeTimeFlags>,Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>)>> SearchWindowInputDataAnalyzers = new();
    
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
        var pathAnalyzer = new PathAnalyzer();
        var imageAnalyzer = new ImageAnalyzer();
        var mathAnalyzer = new MathAnalyzer();
        var knowCommandAnalyzer = new KnowCommandAnalyzer();
        var urlAnalyzer = new UrlAnalyzer();
        SearchWindowInputDataAnalyzers["Kitopia"] =
        [
            (() => pathAnalyzer.AnalyzeTimeFlags, s => pathAnalyzer.AnalyzeInputData(s)),
            (() => imageAnalyzer.AnalyzeTimeFlags, s => imageAnalyzer.AnalyzeInputData(s)),
            (() => mathAnalyzer.AnalyzeTimeFlags, s => mathAnalyzer.AnalyzeInputData(s)),
            (() => knowCommandAnalyzer.AnalyzeTimeFlags, s => knowCommandAnalyzer.AnalyzeInputData(s)),
            (() => urlAnalyzer.AnalyzeTimeFlags, s => urlAnalyzer.AnalyzeInputData(s))
        ];
    }
        
}