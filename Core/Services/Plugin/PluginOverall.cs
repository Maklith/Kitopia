using Core.UI.SearchWindow.InputData;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
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

    public static readonly Dictionary<string, List<Func<InputDataAnalyzeTimeFlags, string?, IEnumerable<InputData>>>>
        SearchWindowInputDataIdentifies = new();

    public static readonly
        Dictionary<string, List<(Func<InputDataAnalyzeTimeFlags>,
            Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>)>> SearchWindowInputDataAnalyzers = new();

    public static List<OnnxModelInfoWrapper> AllOnnxModelInfos =>
        OnnxModelInfos.Values.SelectMany(e => e).ToList();

    public static List<string> AllTargetDevices => OnnxRuntimes.Values.SelectMany(e => e.Keys).ToList();

    public static List<ScreenCaptureExMethod> AllScreenCaptureExMethods =>
        ScreenCaptureExMethods.Values.SelectMany(e => e).ToList();
    
    public static ObservableDictionary<string,ContextMenuItem> ContextMenuItems = new();

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
            new List<Func<InputDataAnalyzeTimeFlags, string?, IEnumerable<InputData>>>
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
        
        
        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KitopiaAvalonia.exe");
        
        ContextMenuItems.Add("kitopia", new ContextMenuItem
        {
            SubItems = [
                new ContextMenuItem
                {
                    Title = "添加到索引",
                    Icon = exePath,
                    Command = exePath,
                    Arguments = StartupArgumentManager.GenerateCmd(StartupAction.IndexAdd, "{0}"),
            
                },
                new ContextMenuItem
                {
                    Title = "文件占用解锁",
                    Icon = exePath,
                    Command = exePath,
                    Arguments = StartupArgumentManager.GenerateCmd(StartupAction.FileLocksmith, "{0}"), // Pass path to FileLocksmith
                },
                new ContextMenuItem
                {
                    Title = "局域网分享",
                    Icon = exePath,
                    Command = exePath,
                    Arguments = StartupArgumentManager.GenerateCmd(StartupAction.LanFileShare, "{all}"),
                }
            ]
        });
        ServiceManager.Services.GetService<IExplorerContextMenuConfiger>()!
            .OverwriteMenuItems(ContextMenuItems.SelectMany(e => e.Value.SubItems).ToList());
    }
    

    public void UpdateContextMenuItems(ObservableDictionary<string, ContextMenuItem> items)
    {
        
    }
}
