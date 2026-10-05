using System.Threading;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class SearchItemScenarioMethod
{
    [ScenarioMethod("lang.kitopiaex.open_or_run_local_item", $"{nameof(item)}=lang.kitopiaex.local_item",
        "return=lang.kitopiaex.return_value", Id = "打开/运行本地项目")]
    public void OpenSearchViewItem(string item, CancellationToken cancellationToken)
    {
        Kitopia.ISearchItemTool.OpenSearchItemByOnlyKey(item);
    }
}