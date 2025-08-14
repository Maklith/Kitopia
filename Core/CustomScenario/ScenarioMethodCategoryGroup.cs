using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Core.SDKs.Tools;
using PluginCore;
using PluginCore.Attribute.Scenario;

namespace Core.SDKs.CustomScenario;

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
        foreach (var (key, value) in CustomScenarioGloble._baseType)
        {
            var String = new ScenarioMethodNode()
            {
                ScenarioMethod = new ScenarioMethod(ScenarioMethodType.默认),
                Title = key
            };
            ObservableCollection<ConnectorItem> StringoutItems = new()
            {
                new ConnectorItem()
                {
                    Source = String,
                    InputObject = new CustomScenarioValue()
                    {
                        Type = value
                    },
                    Title = CustomScenarioGloble.GetI18N(value.FullName),
                 
                    ConnectorType = ConnectorType.Output,
                }
            };
            String.Output = StringoutItems;
            ObservableCollection<ConnectorItem> StringinItems = new()
            {
                new ConnectorItem()
                {
                    Source = String,
                    InputObject = new CustomScenarioValue()
                    {
                        Type = value,
                        Value = value.IsValueType ? Activator.CreateInstance(value) : null,
                        IsSelf = true
                    },
                    Title = CustomScenarioGloble.GetI18N(value.FullName),
                  
                    
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

        var scenarioMethodNode1 = new ScenarioMethod(ScenarioMethodType.判断).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("判断", scenarioMethodNode1);

        var scenarioMethodNode2 = new ScenarioMethod(ScenarioMethodType.一对二).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("一对二", scenarioMethodNode2);

        var scenarioMethodNode3 = new ScenarioMethod(ScenarioMethodType.一对多).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("一对多", scenarioMethodNode3);

        var scenarioMethodNode4 = new ScenarioMethod(ScenarioMethodType.相等).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("相等", scenarioMethodNode4);

        var scenarioMethodNode5 = new ScenarioMethod(ScenarioMethodType.打开运行本地项目).GenerateNode();
        controlScenarioMethodCategoryGroup.Methods.Add("打开运行本地项目", scenarioMethodNode5);


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
                var newScenarioMethodCategoryGroup = new ScenarioMethodCategoryGroup()
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


    private void Clear()
    {
        for (var i = 0; i < Childrens.Count; i++) Childrens.ElementAt(i).Value.Clear();
        Methods.Clear();
        Methods = null;
    }

    public void RemoveMethodsByPluginName(string pluginName)
    {
        if (Childrens.ContainsKey(pluginName)) Childrens[pluginName].Clear();
        Childrens.Remove(pluginName);
        for (var i = 0; i < MixinInfos.Count; i++)
        {
            var target = MixinInfos[i].Target.Split("/");
            var nowScenarioMethodCategoryGroup = RootScenarioMethodCategoryGroup;
            for (var i1 = 0; i1 < target.Length - 1; i1++)
                if (nowScenarioMethodCategoryGroup.Childrens.ContainsKey(target[i1]))
                    nowScenarioMethodCategoryGroup = nowScenarioMethodCategoryGroup.Childrens[target[i1]];
                else throw new ScenarioException("路径不存在");

            nowScenarioMethodCategoryGroup.Methods.Remove(target.Last());
        }

        OnPropertyChanged(nameof(ScenarioMethodCategoryGroup));
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