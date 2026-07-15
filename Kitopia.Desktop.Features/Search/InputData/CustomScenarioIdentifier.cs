using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Search;
using Microsoft.Extensions.DependencyInjection;
using Pinyin.NET;
using PluginCore;
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
    private List<CustomScenarioSearcherWrapper> _cache = new();
    private PinyinSearcher<CustomScenarioSearcherWrapper> _pinyinSearcher;

    public CustomScenarioIdentifier()
    {
        _pinyinSearcher =
            new PinyinSearcher<CustomScenarioSearcherWrapper>(_cache, e => e.CustomScenario.Name);
    }

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        InputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        // if (CustomScenarioManger.CustomScenarios.Select(e=>e.))
        // {

        if (!analyzeTimeFlags.HasFlag(InputDataAnalyzeTimeFlags.InputChanged))
            yield break;
        foreach (var scenario in CustomScenarioManger.CustomScenarios)
        {
            if (_cache.Any(e => e.CustomScenario == scenario)) continue;
            var keys = new List<List<string>>();
            foreach (var key in scenario.Keys) keys.Add([key]);

            var pinyinItem = ServiceManager.Services.GetService<IAppToolService>()!
                .GetPinyin(scenario.Name);
            pinyinItem.Keys!.AddRange(keys);
            scenario.Saved += UpdateCache;
            _cache.Add(new CustomScenarioSearcherWrapper
            {
                CustomScenario = scenario,
                PinyinItem = pinyinItem
            });
        }

        foreach (var searchResultse in _pinyinSearcher.Search(s))
            yield return new PluginCore.SearchWindow.InputData.InputData
            {
                InputType = InputType.情景,
                Data = searchResultse.Source.CustomScenario
            };

        // }
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
