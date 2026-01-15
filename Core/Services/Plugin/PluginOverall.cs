using Core.UI.SearchWindow.InputData;
using Core.Utils;
using PluginCore;
using PluginCore.Onnx;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.Services.Plugin;

public class PluginOverall
{
    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();
    public static readonly Dictionary<string, List<OnnxModelInfoWrapper>> OnnxModelInfos = new();
    public static readonly Dictionary<string, Dictionary<string, Func<IInferenceSession>>> OnnxRuntimes = new();

    public static readonly Dictionary<string, List<Func<InputDataAnalyzeTimeFlags, string, IEnumerable<InputData>>>>
        SearchWindowInputDataIdentifies = new();

    public static readonly
        Dictionary<string, List<(Func<InputDataAnalyzeTimeFlags>,
            Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>)>> SearchWindowInputDataAnalyzers = new();

    public static List<OnnxModelInfoWrapper> AllOnnxModelInfos =>
        OnnxModelInfos.Values.SelectMany(e => e).ToList();

    public static List<string> AllTargetDevices => OnnxRuntimes.Values.SelectMany(e => e.Keys).ToList();

    public static List<ScreenCaptureExMethod> AllScreenCaptureExMethods =>
        ScreenCaptureExMethods.Values.SelectMany(e => e).ToList();

    public static Func<IInferenceSession>? GetOnnxRuntime(string targetDevice)
    {
        var firstOrDefault = OnnxRuntimes.Values.SelectMany(e => e).FirstOrDefault(e => e.Key == targetDevice);

        return firstOrDefault.Value ?? null;
    }

    static PluginOverall()
    {
        var customScenarioIdentifier = new CustomScenarioIdentifier();
        var urlIdentifier = new UrlIdentifier();
        var knowCommandIdentifier = new KnowCommandIdentifier();
        var mathIdentifier = new MathIdentifier();
        var imageIdentifier = new ImageIdentifier();
        var pathIdentifier = new PathIdentifier();
        SearchWindowInputDataIdentifies["Kitopia"] =
            new List<Func<InputDataAnalyzeTimeFlags, string, IEnumerable<InputData>>>
            {
                (flags, s) => pathIdentifier.IdentifyInputData(flags, s),
                (flags, s) => imageIdentifier.IdentifyInputData(flags, s),
                (flags, s) => mathIdentifier.IdentifyInputData(flags, s),
                (flags, s) => knowCommandIdentifier.IdentifyInputData(flags, s),
                (flags, s) => urlIdentifier.IdentifyInputData(flags, s),
                (flags, s) => customScenarioIdentifier.IdentifyInputData(flags, s)
            };
        var pathAnalyzer = new PathAnalyzer();
        var imageAnalyzer = new ImageAnalyzer();
        var mathAnalyzer = new MathAnalyzer();

        var knowCommandAnalyzer = new KnowCommandAnalyzer();
        var urlAnalyzer = new UrlAnalyzer();
        var customScenarioAnalyzer = new CustomScenarioAnalyzer();
        SearchWindowInputDataAnalyzers["Kitopia"] =
        [
            (() => pathAnalyzer.AnalyzeTimeFlags, s => pathAnalyzer.AnalyzeInputData(s)),
            (() => imageAnalyzer.AnalyzeTimeFlags, s => imageAnalyzer.AnalyzeInputData(s)),
            (() => mathAnalyzer.AnalyzeTimeFlags, s => mathAnalyzer.AnalyzeInputData(s)),
            (() => knowCommandAnalyzer.AnalyzeTimeFlags, s => knowCommandAnalyzer.AnalyzeInputData(s)),
            (() => urlAnalyzer.AnalyzeTimeFlags, s => urlAnalyzer.AnalyzeInputData(s)),
            (() => customScenarioAnalyzer.AnalyzeTimeFlags, s => customScenarioAnalyzer.AnalyzeInputData(s))
        ];
    }
}