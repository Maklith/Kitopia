using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class UrlAnalyzer : IInputDataAnalyzer
{
    public IInputDataAnalyzeTimeFlags AnalyzeTimeFlags => IInputDataAnalyzeTimeFlags.搜索前 |
                                                          IInputDataAnalyzeTimeFlags.仅有搜索内容打开时 |
                                                          IInputDataAnalyzeTimeFlags.搜索时;

    public IEnumerable<SearchViewItem> AnalyzeInputData(
        IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
            if (inputData.InputType == InputType.网址)
                yield return new SearchViewItem
                {
                    ItemDisplayName = $"打开网页:{inputData.Data}",
                    FileType = FileType.URL,
                    OnlyKey = (string)inputData.Data,
                    Icon = null,
                    IconSymbol = 62555,
                    IsVisible = true
                };
    }
}