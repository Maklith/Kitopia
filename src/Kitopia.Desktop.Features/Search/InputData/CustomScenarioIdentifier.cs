using Pinyin.NET;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Search.InputProcessing;

public class CustomScenarioSearcherWrapper
{
    public PinyinItem PinyinItem { get; set; }
    public Kitopia.Desktop.Features.CustomScenario.CustomScenario CustomScenario { get; set; }
}

public class CustomScenarioIdentifier : IInputDataIdentifier
{
    private readonly List<CustomScenarioSearcherWrapper> _cache = new();

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        if (!analyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.InputChanged) || string.IsNullOrWhiteSpace(s))
            yield break;

        yield break;
    }

    public void UpdateCache(object? sender, EventArgs eventArgs)
    {
        if (sender is Kitopia.Desktop.Features.CustomScenario.CustomScenario scenario)
        {
            var customScenarioSearcherWrapper = _cache.Find(e => e.CustomScenario == scenario);
            if (customScenarioSearcherWrapper != null) _cache.Remove(customScenarioSearcherWrapper);
        }
    }
}
