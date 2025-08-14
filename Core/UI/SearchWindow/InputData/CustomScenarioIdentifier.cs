using Core.CustomScenario;
using Core.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using Pinyin.NET;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.UI.SearchWindow.InputData;

public class CustomScenarioSearcherWrapper
{
    public PinyinItem PinyinItem { get; set; }
    public CustomScenario.CustomScenario CustomScenario { get; set; }
}

public class CustomScenarioIdentifier : IInputDataIdentifier
{
    private List<CustomScenarioSearcherWrapper> _cache = new();
    private PinyinSearcher<CustomScenarioSearcherWrapper> _pinyinSearcher;

    public CustomScenarioIdentifier()
    {
        _pinyinSearcher =
            new PinyinSearcher<CustomScenarioSearcherWrapper>(_cache, nameof(CustomScenarioSearcherWrapper.PinyinItem));
    }

    public void UpdateCache(object? sender, EventArgs eventArgs)
    {
        if (sender is CustomScenario.CustomScenario scenario)
        {
            var customScenarioSearcherWrapper = _cache.Find(e => e.CustomScenario == scenario);
            if (customScenarioSearcherWrapper != null) _cache.Remove(customScenarioSearcherWrapper);
        }
    }

    public IEnumerable<PluginCore.SearchWindow.InputData.InputData> IdentifyInputData(
        IInputDataAnalyzeTimeFlags analyzeTimeFlags, string? s)
    {
        // if (CustomScenarioManger.CustomScenarios.Select(e=>e.))
        // {

        if (!analyzeTimeFlags.HasFlag(IInputDataAnalyzeTimeFlags.搜索时))
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
        yield break;
    }
}