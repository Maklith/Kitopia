
using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SearchWindow.InputData;

public class UrlAnalyzer : IInputDataAnalyzer
{
    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<ViewModel.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
        {
            if (inputData.InputType == InputType.网址)
            {
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
    }
}