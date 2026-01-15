using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class KnowCommandAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputChanged;

    public IEnumerable<SearchViewItem> AnalyzeInputData(
        IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
            if (inputData.InputType == InputType.命令)
            {
                var originalValue = (string)inputData.Data;
                yield return new SearchViewItem
                {
                    ItemDisplayName = "执行命令:" + originalValue,
                    FileType = FileType.命令,
                    OnlyKey = originalValue,
                    Icon = null,
                    IconSymbol = 61039,
                    IsVisible = true
                };

                yield break;
            }
    }
}