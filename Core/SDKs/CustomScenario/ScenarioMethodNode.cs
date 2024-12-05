using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.JsonConverter;
using Core.SDKs.CustomType;
using Core.SDKs.Services;
using Core.SDKs.Services.Config;
using Core.SDKs.Services.Plugin;
using Core.SDKs.Tools.Ex;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace Core.SDKs.CustomScenario;

public enum S节点状态
{
    未验证,
    已验证,
    错误,
    初步验证
}

public partial class ScenarioMethodNode : ObservableRecipient
{
    [property: JsonConverter(typeof(PointJsonConverter))]
    [JsonConverter(typeof(PointJsonConverter))]
    [ObservableProperty]
    private Point _location;

    [ObservableProperty] private string _title;
    [ObservableProperty] private ObservableCollection<ConnectorItem> input = new();
    [ObservableProperty] private ObservableCollection<ConnectorItem> output = new();
    [ObservableProperty] private S节点状态 status = S节点状态.未验证;

    [JsonConverter(typeof(ScenarioMethodJsonCtr))]
    public ScenarioMethod ScenarioMethod { get; set; }

    public bool Invoke(CancellationToken cancellationToken, ObservableCollection<ConnectionItem> connections,
        ObservableDictionary<string, CustomScenarioValue> values)
    {
        //生成本节点所有数据
        switch (ScenarioMethod.Type)
        {
            case ScenarioMethodType.插件方法:
            {
                List<object> list = new();
                var index = 1;
                foreach (var parameterInfo in ScenarioMethod.Method.GetParameters())
                {
                    if (parameterInfo.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                    {
                        var autoUnboxIndex = Input[index].AutoUnboxIndex;
                        var parameterList = new List<object>();
                        List<Type> parameterTypesList = new();
                        while (Input.Count >= index && Input[index].AutoUnboxIndex == autoUnboxIndex)
                        {
                            var item = Input[index].InputObject;
                            if (item != null)
                            {
                                parameterList.Add(item);
                                parameterTypesList.Add(item.GetType());
                            }
                            else
                            {
                                return false;
                            }

                            index++;
                        }

                        var instance = parameterInfo.ParameterType.GetConstructor(parameterTypesList.ToArray())
                            ?.Invoke(parameterList.ToArray());
                        if (instance != null)
                        {
                            list.Add(instance);
                        }
                        else
                        {
                            return false;
                        }

                        continue;
                    }

                    if (index == Input.Count)
                    {
                        list.Add(cancellationToken);
                        break;
                    }

                    var inputObject = Input[index].InputObject;
                    if (inputObject != null)
                    {
                        list.Add(inputObject);
                    }
                    else
                    {
                        return false;
                    }

                    index++;
                }

                var invoke = ScenarioMethod.Method.Invoke(
                    ScenarioMethod.ServiceProvider!.GetService(ScenarioMethod.Method.DeclaringType!),
                    list.ToArray());
                if (invoke is null)
                    return false;
                if (ScenarioMethod.Method.ReturnParameter.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not
                    null)
                {
                    var type = ScenarioMethod.Method.ReturnParameter.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    {
                        foreach (var connectorItem in Output)
                        {
                            if (connectorItem.Type == memberInfo.PropertyType)
                            {
                                var value = invoke.GetType()
                                    .InvokeMember(memberInfo.Name,
                                        BindingFlags.Instance | BindingFlags.IgnoreCase |
                                        BindingFlags.Public | BindingFlags.NonPublic |
                                        BindingFlags.GetProperty, null, invoke, null);

                                connectorItem.InputObject = value;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (Output.Any())
                    {
                        Output[1].InputObject = invoke;
                    }
                }

                break;
            }
            case ScenarioMethodType.一对二:
            {
                Output[0].InputObject = "流1";
                Output[1].InputObject = "流2";
                break;
            }
            case ScenarioMethodType.一对多:
            {
                for (var i = 0; i < Output.Count; i++)
                {
                    Output[i].InputObject = $"流{i + 1}";
                }

                break;
            }
            case ScenarioMethodType.相等:
            {
                if (Input[1].InputObject is null)
                {
                    Output[0].InputObject = false;
                }
                else if (Input[2].InputObject is null)
                {
                    Output[0].InputObject = false;
                }
                else
                {
                    Output[0].InputObject = Input[1].InputObject!.Equals(Input[2].InputObject);
                }

                break;
            }
            case ScenarioMethodType.变量设置:
            {
                if (values.ContainsKey(ScenarioMethod.ValueName))
                {
                    values[ScenarioMethod.ValueName].Value = Input[1].InputObject!;
                    
                }

                break;
            }
            case ScenarioMethodType.变量获取:
            {
                if (values.ContainsKey(ScenarioMethod.ValueName))
                {
                    Output[1].InputObject = values[ScenarioMethod.ValueName].Value;
                }

                break;
            }
            case ScenarioMethodType.判断:
            {
                if (Input[1].InputObject is bool b1)
                {
                    if (b1)
                    {
                        Output[0].IsNotUsed = false;
                        Output[0].InputObject = "当前流";
                        Output[1].IsNotUsed = true;
                        Output[1].InputObject = "未使用的流";
                    }
                    else
                    {
                        Output[0].IsNotUsed = true;
                        Output[0].InputObject = "未使用的流";
                        Output[1].IsNotUsed = false;
                        Output[1].InputObject = "当前流";
                    }
                }

                break;
            }
            case ScenarioMethodType.打开运行本地项目:
            {
                if (Input.Count() >= 3)
                {
                    List<object> parameterList = new();
                    for (var index = 2; index < Input.Count; index++)
                    {
                        parameterList.Add(Input[index].InputObject);
                    }

                    ServiceManager.Services.GetService<ISearchItemTool>()
                        .OpenSearchItemByOnlyKey((string)Input[1].InputObject,
                            parameterList.ToArray());
                }
                else
                {
                    ServiceManager.Services.GetService<ISearchItemTool>()
                        .OpenSearchItemByOnlyKey((string)Input[1].InputObject);
                }

                break;
            }
            case ScenarioMethodType.默认:
            {
                if (Input == null || Input.Count == 0)
                {
                    break;
                }

                var connectorItem = Input.First(e => e.RealType != typeof(NodeConnectorClass));
                if (connectorItem == null)
                {
                    break;
                }

                foreach (var item in Output)
                {
                    item.InputObject = connectorItem.InputObject;
                }

                break;
            }
        }

        //将节点数据赋值给下一个节点
        foreach (var connectorItem in Output)
        {
            if (connectorItem.RealType == typeof(NodeConnectorClass))
            {
                continue;
            }

            foreach (var sourceOrNextConnectorItem in connectorItem.GetSourceOrNextConnectorItems(connections))
            {
                sourceOrNextConnectorItem.InputObject = connectorItem.InputObject;
            }
        }

        return true;
    }

    public ScenarioMethodNode Copy(Dictionary<string, int> pluginUsedCount)
    {
        var item = new ScenarioMethodNode
        {
            Title = this.Title,
            ScenarioMethod = this.ScenarioMethod,
            Location = new Point(Location.X, Location.Y)
        };
        if (ScenarioMethod.IsFromPlugin)
        {
            pluginUsedCount.AddOrIncrease(ScenarioMethod.PluginInfo!.ToPlgString());
        }

        ObservableCollection<ConnectorItem> input = new();
        foreach (var connectorItem in Input)
        {
            input.Add(new ConnectorItem
            {
                Anchor = new Point(connectorItem.Anchor.X, connectorItem.Anchor.Y),
                Source = item,
                TypeName = connectorItem.TypeName,
                Title = connectorItem.Title,
                Type = connectorItem.Type,
                RealType = connectorItem.RealType,
                InputObject = connectorItem.InputObject,
                AutoUnboxIndex = connectorItem.AutoUnboxIndex,
                IsSelf = connectorItem.IsSelf,
                SelfInputAble = connectorItem.SelfInputAble,
                IsOut = connectorItem.IsOut,
                isPluginInputConnector = connectorItem.isPluginInputConnector,
                PluginInputConnector = connectorItem.PluginInputConnector
            });
            var plugin = PluginManager.EnablePlugin.FirstOrDefault((e) => e.Value._dll == connectorItem.Type.Assembly)
                .Value;
            if (plugin is not null)
            {
                pluginUsedCount.AddOrIncrease(ScenarioMethod.PluginInfo!.ToPlgString());
            }

            var plugin2 = PluginManager.EnablePlugin
                .FirstOrDefault((e) => e.Value._dll == connectorItem.RealType.Assembly)
                .Value;
            if (plugin2 is not null)
            {
                pluginUsedCount.AddOrIncrease(ScenarioMethod.PluginInfo!.ToPlgString());
            }
        }

        ObservableCollection<ConnectorItem> output = new();
        foreach (var connectorItem in Output)
        {
            var connectorItem1 = new ConnectorItem
            {
                Anchor = new Point(connectorItem.Anchor.X, connectorItem.Anchor.Y),
                Source = item,
                Title = connectorItem.Title,
                TypeName = connectorItem.TypeName,
                RealType = connectorItem.RealType,
                AutoUnboxIndex = connectorItem.AutoUnboxIndex,
                Type = connectorItem.Type,
                IsConnected = connectorItem.IsConnected,
                IsOut = connectorItem.IsOut
            };
            if (connectorItem.Interfaces is { Count: > 0 })
            {
                List<string> interfaces = new();
                foreach (var connectorItemInterface in connectorItem.Interfaces)
                {
                    interfaces.Add(connectorItemInterface);
                }

                connectorItem1.Interfaces = interfaces;
            }

            var plugin = PluginManager.EnablePlugin.FirstOrDefault((e) => e.Value._dll == connectorItem.Type.Assembly)
                .Value;
            if (plugin is not null)
            {
                pluginUsedCount.AddOrIncrease(ScenarioMethod.PluginInfo!.ToPlgString());
            }

            var plugin2 = PluginManager.EnablePlugin
                .FirstOrDefault((e) => e.Value._dll == connectorItem.RealType.Assembly)
                .Value;
            if (plugin2 is not null)
            {
                pluginUsedCount.AddOrIncrease(ScenarioMethod.PluginInfo!.ToPlgString());
            }

            output.Add(connectorItem1);
        }

        item.Input = input;
        item.Output = output;
        return item;
    }
}