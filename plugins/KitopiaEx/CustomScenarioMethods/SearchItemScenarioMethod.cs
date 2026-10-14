using System.Threading;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class SearchItemScenarioMethod
{
    [ScenarioMethod("lang.kitopiaex.open_or_run_local_item", $"{nameof(item)}=lang.kitopiaex.local_item",
        Id = "打开/运行本地项目", SupportsLocalItemInputs = true)]
    public void OpenSearchViewItem([SelfInput] string item, object[]? inputValues = null,
        CancellationToken cancellationToken = default)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(item);
        cancellationToken.ThrowIfCancellationRequested();
        Kitopia.ISearchItemTool.OpenSearchItemByOnlyKey(item, inputValues ?? System.Array.Empty<object>());
    }
}
