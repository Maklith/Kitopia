using Core.SDKs.CustomScenario;
using Core.ViewModel;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SearchWindow.InputData;

public class CustomScenarioAnalyzer : IInputDataAnalyzer
{
    private IInputDataAnalyzeTimeFlags _analyzeTimeFlags=IInputDataAnalyzeTimeFlags.搜索时;

    public IInputDataAnalyzeTimeFlags AnalyzeTimeFlags => _analyzeTimeFlags;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        if (!inputDatas.Any())
            yield break;
        foreach (var inputData in inputDatas)
        {
            if (inputData.InputType is InputType.情景)
            {
                if (inputData.Data is CustomScenario scenario)
                {
                    yield return new SearchViewItem()
                    {
                        ItemDisplayName = $"运行情景:'{scenario.Name}'",
                        FileType = FileType.自定义情景,
                        IconSymbol = 0xF78B,
                        Icon = null,
                        IsVisible = true
                    };
                }
            }
        }
        yield break;
    }
}