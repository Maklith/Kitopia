using System.Collections.Generic;
using PluginCore;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class CustomScenarioAnalyzer : IInputDataAnalyzer
{
    public InputDataAnalyzeTimeFlags AnalyzeTimeFlags => InputDataAnalyzeTimeFlags.InputChanged;

    public IEnumerable<SearchViewItem> AnalyzeInputData(
        IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
            if (inputData.InputType is InputType.情景)
                if (inputData.Data is CustomScenario.CustomScenario scenario)
                    yield return new SearchViewItem
                    {
                        ItemDisplayName = $"运行情景:'{scenario.Name}'",
                        FileType = FileType.自定义情景,
                        IconSymbol = 0xF78B,
                        Icon = null,
                        IsVisible = true
                    };

        yield break;
    }
}