// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaEx
// FileName:SetTopmostWindowAnalyzer.cs
// Date: 2026/01/12 16:01
// FileEffect:

using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace KitopiaEx.SearchWindow.InputDataAnalyzer;

public class SetTopmostWindowAnalyzer : IInputDataAnalyzer
{
    public IInputDataAnalyzeTimeFlags AnalyzeTimeFlags => IInputDataAnalyzeTimeFlags.仅用作文本索引;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<InputData> inputDatas)
    {
        yield return new SearchViewItem
        {
            ItemDisplayName = "置顶任意窗口",
            OnlyKey = "KitopiaEx_SetTopmostWindowAnalyzer_Action",
            FileType = FileType.自定义,
            IconSymbol = 0xf602,
            Icon = null,
            IsVisible = true,
            ShowAsMiniApp = false,
            Action = (item, s) =>
            {
                Kitopia.ServiceProvider.GetService<IWindowTool>()!.SelectAndSetWindowTopMost();
            }
        };
        
    }
}