using PluginCore.CustomScenario.Attribute.Scenario;

namespace KitopiaEx;
[AutoUnbox]
public class Aut1
{
    [AutoUnboxProperty]
    public int Id { get; set; } = 1;
    [AutoUnboxProperty]
    public string Name { get; set; } = "自动化1";
}
public class Test1
{
    // [ScenarioMethod("Test", $"{nameof(item)}=本地项目",
    //     "return=返回参数")]
    // public Aut1 OpenSearchViewItem(string a,Aut1 item,string b, CancellationToken cancellationToken)
    // {
    //     item.Id += 1;
    //     item.Name = $"{item.Name} - {a} - {b}";
    //     return item;
    // }
}