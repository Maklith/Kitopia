using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Onnx;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginOverall
{
    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();
    public static readonly Dictionary<string, List<OnnxModelInfoWrapper>> OnnxModelInfos = new();
    public static readonly Dictionary<string, Dictionary<string, Func<IInferenceSession>>> OnnxRuntimes = new();

    public static readonly ConcurrentDictionary<string, List<Func<InputDataAnalyzeTimeFlags, string?, IEnumerable<InputData>>>>
        SearchWindowInputDataIdentifies = new();

    public static readonly
        ConcurrentDictionary<string, List<(Func<InputDataAnalyzeTimeFlags>,
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
        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kitopia.Desktop.exe");
        
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
