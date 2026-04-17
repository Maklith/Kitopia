
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName: WindowSwitcherAnalyzer.cs

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaEx.SearchWindow.InputDataAnalyzer;

public class WindowSwitcherAnalyzer : IInputDataAnalyzer
{
    // Enable analysis during search
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.WindowOpenUpdateIndex;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<InputData> inputDatas)
    {
        
        
        var windowTool = Kitopia.ServiceProvider.GetService<IWindowTool>();

        if (windowTool == null)
            yield break;

        // Get all windows
        var windows = windowTool.GetAllWindows();
        
        foreach (var window in windows)
        {
            if (string.IsNullOrWhiteSpace(window.Title)) continue;
           
            yield return new SearchViewItem
            {
                ItemDisplayName = window.Title,
                // Use Application type so that the system tries to extract icon from OnlyKey (ModuleFileName)
                FileType = FileType.窗口, 
                OnlyKey = window.ModuleFileName ?? "",
                // Fallback icon symbol (Window icon)
                ShowAsMiniApp = false,
                
                Action = (item, s) =>
                {
                    windowTool.SetForegroundWindow(window.Hwnd);
                },
                GetIconAction = (item) => windowTool.GetWindowIcon(window.Hwnd)
            }; 
        }
    }
}
