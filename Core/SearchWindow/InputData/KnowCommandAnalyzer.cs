using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SearchWindow.InputData;

public class KnowCommandAnalyzer : IInputDataAnalyzer
{

    public IInputDataAnalyzeTimeFlags AnalyzeTimeFlags => IInputDataAnalyzeTimeFlags.搜索时;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<ViewModel.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
        {
            if (inputData.InputType==InputType.命令)
            {
                string originalValue = (string)inputData.Data;
                yield return  new SearchViewItem
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
}