using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Search.InputProcessing;

public class UrlAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputEmpty |
                                                          InputDataAnalyzeTimeFlags.WindowShow |
                                                          InputDataAnalyzeTimeFlags.InputChanged;

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
