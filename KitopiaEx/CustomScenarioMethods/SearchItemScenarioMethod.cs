using System.Threading;
using PluginCore;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx.CustomScenarioMethods;

public class SearchItemScenarioMethod
{
    [ScenarioMethod("打开/运行本地项目", $"{nameof(item)}=本地项目",
        "return=返回参数")]
    public void OpenSearchViewItem(string item, CancellationToken cancellationToken)
    {
        Kitopia.ISearchItemTool.OpenSearchItemByOnlyKey(item);
    }
}