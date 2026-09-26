using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace Kitopia.Desktop.Features.CustomScenario;

public class ScenarioMethodCategoryGroup : INotifyPropertyChanged
{
    public static ScenarioMethodCategoryGroup RootScenarioMethodCategoryGroup = GenBaseScenarioMethodCategoryGroup();

    private static ScenarioMethodCategoryGroup GenBaseScenarioMethodCategoryGroup()
    {
        var rootScenarioMethodCategoryGroup = new ScenarioMethodCategoryGroup();

        var scenarioMethodCategoryGroup = new ScenarioMethodCategoryGroup();
        rootScenarioMethodCategoryGroup.Childrens.Add("Kitopia", scenarioMethodCategoryGroup);
        scenarioMethodCategoryGroup.Name = "Kitopia";
        //基本数值类型
        var valueScenarioMethodCategoryGroup = new ScenarioMethodCategoryGroup();
        valueScenarioMethodCategoryGroup.Name = "基本数据类型";
        scenarioMethodCategoryGroup.Childrens.Add("基本数据类型", valueScenarioMethodCategoryGroup);
        foreach (var (key, value) in CustomScenarioGlobe._baseType)
        {
            var String = new ScenarioMethodNode
            {
                ScenarioMethod = new ScenarioMethod(ScenarioMethodType.Default),
                Title = key
            };
            ObservableCollection<ConnectorItem> StringoutItems = new()
            {
                new ConnectorItem
                {
                    Source = String,
                    InputObject = new CustomScenarioValue
                    {
                        SerializeType = value
                    },
                    Title = CustomScenarioGlobe.GetI18N(value.FullName),

                    ConnectorType = ConnectorType.Output
                }
            };
            String.Output = StringoutItems;
            ObservableCollection<ConnectorItem> StringinItems = new()
            {
                new ConnectorItem
                {
                    Source = String,
                    InputObject = new CustomScenarioValue
                    {
                        SerializeType = value,
                        Value = value.IsValueType ? Activator.CreateInstance(value) : null,
                        IsSelf = true
                    },
                    Title = CustomScenarioGlobe.GetI18N(value.FullName)
                }
            };
            if (value.FullName == "System.Int32") StringinItems[0].InputObject.Value = (double)0;

            String.Input = StringinItems;
            valueScenarioMethodCategoryGroup.Methods.Add(key, String);
        }

        //节点控制
        var controlScenarioMethodCategoryGroup = new ScenarioMethodCategoryGroup();
        scenarioMethodCategoryGroup.Childrens.Add("节点控制", controlScenarioMethodCategoryGroup);
        controlScenarioMethodCategoryGroup.Name = "节点控制";

        var scenarioMethodNode1 = new ScenarioMethod(ScenarioMethodType.Condition).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("Condition", scenarioMethodNode1);

        var scenarioMethodNode2 = new ScenarioMethod(ScenarioMethodType.OneToTwo).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("OneToTwo", scenarioMethodNode2);

        var scenarioMethodNode3 = new ScenarioMethod(ScenarioMethodType.OneToMany).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("OneToMany", scenarioMethodNode3);

        var scenarioMethodNode4 = new ScenarioMethod(ScenarioMethodType.Equal).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("Equal", scenarioMethodNode4);

        var scenarioMethodNode5 = new ScenarioMethod(ScenarioMethodType.OpenRunLocalProject).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("OpenRunLocalProject", scenarioMethodNode5);


        return rootScenarioMethodCategoryGroup;
    }

    public static ScenarioMethodCategoryGroup GetScenarioMethodCategoryGroupByAttribute(
        ScenarioMethodCategoryAttribute attribute, ScenarioMethodCategoryGroup? scenarioMethodCategoryGroup = null)
    {
        var strings = attribute.Name.Split("/");
        var nowScenarioMethodCategoryGroup = attribute.IsMixinOrTopCategory
            ? RootScenarioMethodCategoryGroup
            : scenarioMethodCategoryGroup ?? RootScenarioMethodCategoryGroup;
        for (var index = 0; index < strings.Length; index++)
        {
            var se = strings[index];
            if (nowScenarioMethodCategoryGroup.Childrens.ContainsKey(se))
            {
                nowScenarioMethodCategoryGroup = nowScenarioMethodCategoryGroup.Childrens[se];
            }
            else
            {
                var newScenarioMethodCategoryGroup = new ScenarioMethodCategoryGroup
                {
                    Name = se,
                    Parent = nowScenarioMethodCategoryGroup
                };
                nowScenarioMethodCategoryGroup.Childrens.Add(se, newScenarioMethodCategoryGroup);
                nowScenarioMethodCategoryGroup = newScenarioMethodCategoryGroup;
                if (index == strings.Length - 1) nowScenarioMethodCategoryGroup.DisplayName = attribute.Name;
            }
        }

        return nowScenarioMethodCategoryGroup;
    }

    public List<MixinInfo> MixinInfos = new();
    public ScenarioMethodCategoryGroup? Parent { get; set; }
    public string Name { get; set; }

    public string DisplayName { get; set; }

    //
    public Dictionary<string, ScenarioMethodCategoryGroup> Childrens { get; set; } = new();
    public Dictionary<string, ScenarioMethodNode> Methods { get; set; } = new();

    public ScenarioMethodCategoryGroup? GetParent()
    {
        return Parent;
    }

    public ScenarioMethodCategoryGroup GetRoot()
    {
        return Parent?.GetRoot() ?? this;
    }


    public void RemoveMethodsByPluginName(string pluginName)
    {
        bool RemoveFromGroup(ScenarioMethodCategoryGroup group)
        {
            var removed = false;
            foreach (var (key, node) in group.Methods.ToArray())
                if (node.ScenarioMethod.PluginInfo?.ToPlgString() == pluginName)
                    removed |= group.Methods.Remove(key);

            foreach (var (key, child) in group.Childrens.ToArray())
            {
                if (!RemoveFromGroup(child)) continue;
                removed = true;
                if (child.Methods.Count == 0 && child.Childrens.Count == 0)
                    group.Childrens.Remove(key);
            }

            return removed;
        }

        if (RemoveFromGroup(this)) OnPropertyChanged(nameof(ScenarioMethodCategoryGroup));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// 
/// <param name="Target">
/// 其他插件名称/分类1/.../方法绝对名称<see cref="ScenarioMethod.MethodAbsolutelyName"/>
/// </param>
/// 
public struct MixinInfo
{
    public string Source { get; set; }
    public string Target { get; set; }
}
