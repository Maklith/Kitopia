using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.Infrastructure.JsonConverter;
using Core.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Attribute;

namespace Core.CustomScenario;

/// <summary>
/// 节点状态枚举，表示场景节点的验证和执行状态
/// Node status enumeration representing the validation and execution state of scenario nodes
/// </summary>
public enum NodeStatus
{
    /// <summary>未验证状态 / Unverified state</summary>
    Unverified,

    /// <summary>已验证状态 / Verified state</summary>
    Verified,

    /// <summary>错误状态 / Error state</summary>
    Error,

    /// <summary>初步验证状态 / Preliminary verified state</summary>
    PreliminaryVerified
}

[JsonDerivedType(typeof(ScenarioNodeBase), "base")]
[JsonDerivedType(typeof(ScenarioMethodNode), "ScenarioMethodNode")]
[JsonDerivedType(typeof(KnotNodeViewModel), "KnotNode")]
public partial class ScenarioNodeBase : ObservableRecipient
{
    [property: JsonConverter(typeof(PointJsonConverter))]
    [JsonConverter(typeof(PointJsonConverter))]
    [ObservableProperty]
    private Point _location;

    [ObservableProperty] private string _title;
    [ObservableProperty] private NodeStatus status = NodeStatus.Unverified;

    public virtual bool Invoke(CancellationToken cancellationToken, ObservableCollection<ConnectionItem> connections,
        ObservableDictionary<string, CustomScenarioValue> values,
        ObservableDictionary<string, CustomScenarioValue> tempValues,
        ObservableDictionary<string, CustomScenarioValue> inputValues)
    {
        return false;
    }

    public virtual IEnumerable<ScenarioNodeBase> GetForwardNodes(
        ObservableCollection<ConnectionItem> connections)
    {
        yield break;
    }

    public virtual IEnumerable<ScenarioNodeBase> GetBackwardNodes(
        ObservableCollection<ConnectionItem> connections)
    {
        yield break;
    }

    public virtual bool InputDataIsEnough(ObservableCollection<ConnectionItem> connections)
    {
        return false;
    }

    public virtual bool IsUsed(ObservableCollection<ConnectionItem> connections)
    {
        return false;
    }

    public virtual void ResetData()
    {
    }

    public virtual void ConnectorInit()
    {
    }

    public virtual bool IsUseThePlugin(string plugStr)
    {
        return false;
    }

    public virtual ScenarioNodeBase Copy()
    {
        return null;
    }
}

public partial class KnotNodeViewModel : ScenarioNodeBase
{
    [ObservableProperty] private ConnectorItem connector = new();

    public override bool InputDataIsEnough(ObservableCollection<ConnectionItem> connections)
    {
        return connections.Any(e => e.Target == Connector);
    }

    public override bool Invoke(CancellationToken cancellationToken, ObservableCollection<ConnectionItem> connections,
        ObservableDictionary<string, CustomScenarioValue> values,
        ObservableDictionary<string, CustomScenarioValue> tempValues,
        ObservableDictionary<string, CustomScenarioValue> inputValues)
    {
        foreach (var b in connections.Where(e => e.Source == connector))
            b.Target.InputObject.Value =
                b.Source.InputObject.Value; //将连接的值传递给下一个节点

        return true;
    }

    public override IEnumerable<ScenarioNodeBase> GetForwardNodes(
        ObservableCollection<ConnectionItem> connections)
    {
        foreach (var sourceSource in Connector.GetSourceOrNextPointItems(connections, false)) yield return sourceSource;
    }

    public override IEnumerable<ScenarioNodeBase> GetBackwardNodes(
        ObservableCollection<ConnectionItem> connections)
    {
        foreach (var sourceSource in Connector.GetSourceOrNextPointItems(connections, true)) yield return sourceSource;
    }

    public override bool IsUsed(ObservableCollection<ConnectionItem> connections)
    {
        return connections.Any(e => e.Source == connector || e.Target == connector);
    }

    public override ScenarioNodeBase Copy()
    {
        var item = new KnotNodeViewModel
        {
            Title = Title,
            Location = new Point(Location.X, Location.Y)
        };
        item.Connector = new ConnectorItem
        {
            Anchor = new Point(Connector.Anchor.X, Connector.Anchor.Y),
            Source = item,
            InputObject = new CustomScenarioValue
            {
                RealType = Connector.InputObject.RealType,
                Type = Connector.InputObject.Type,
                Value = Connector.InputObject.Value
            }
        };
        return item;
    }
}

public partial class ScenarioMethodNode : ScenarioNodeBase
{
    [JsonIgnore] [property: JsonIgnore] [ObservableProperty]
    private TimeSpan _invokeTime = TimeSpan.Zero;

    [ObservableProperty] private ObservableCollection<ConnectorItem> input = new();
    [ObservableProperty] private ObservableCollection<ConnectorItem> output = new();

    [JsonConverter(typeof(ScenarioMethodJsonCtr))]
    public ScenarioMethod ScenarioMethod { get; set; }

    public override bool Invoke(CancellationToken cancellationToken, ObservableCollection<ConnectionItem> connections,
        ObservableDictionary<string, CustomScenarioValue> values,
        ObservableDictionary<string, CustomScenarioValue> tempValues,
        ObservableDictionary<string, CustomScenarioValue> inputValues)
    {
        var start = DateTime.Now;
        //生成本节点所有数据
        switch (ScenarioMethod.Type)
        {
            case ScenarioMethodType.PluginMethod:
            {
                List<object> list = new();
                var index = 1;
                foreach (var parameterInfo in ScenarioMethod.Method.GetParameters())
                {
                    if (parameterInfo.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                    {
                        var autoUnboxIndex = Input[index].AutoUnboxIndex;
                        var instance = parameterInfo.ParameterType.GetConstructor([])
                            ?.Invoke([]);
                        if (instance is null)
                            return false;

                        while (Input.Count > index && Input[index].AutoUnboxIndex == autoUnboxIndex)
                        {
                            var item = Input[index].InputObject;
                            parameterInfo.ParameterType.GetProperty(Input[index].AutoUnboxPropertyName)
                                .SetValue(instance, item.Value);
                            index++;
                        }

                        list.Add(instance);
                        continue;
                    }

                    if (index == Input.Count)
                    {
                        list.Add(cancellationToken);
                        break;
                    }

                    if (Input[index].isPluginInputConnector)
                    {
                        list.Add(Input[index].InputObject.Value);
                    }
                    else
                    {
                        var inputObject = Input[index].InputObject.Value;
                        if (inputObject != null)
                            list.Add(inputObject);
                        else
                            return false;
                    }


                    index++;
                }

                var invoke = ScenarioMethod.Method.Invoke(
                    ScenarioMethod.ServiceProvider!.GetService(ScenarioMethod.Method.DeclaringType!),
                    list.ToArray());
                if (invoke is null)
                    break;
                if (ScenarioMethod.Method.ReturnParameter.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not
                    null)
                {
                    var type = ScenarioMethod.Method.ReturnParameter.ParameterType;
                    foreach (var memberInfo in type.GetProperties())
                    foreach (var connectorItem in Output)
                        if (connectorItem.InputObject.Type == memberInfo.PropertyType)
                        {
                            var value = invoke.GetType()
                                .InvokeMember(memberInfo.Name,
                                    BindingFlags.Instance | BindingFlags.IgnoreCase |
                                    BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.GetProperty, null, invoke, null);

                            connectorItem.InputObject.Value = value;
                            break;
                        }
                }
                else
                {
                    if (Enumerable.Count<ConnectorItem>(Output) >= 2) Output[1].InputObject.Value = invoke;
                }

                break;
            }
            case ScenarioMethodType.OneToTwo:
            {
                Output[0].InputObject.Value = "流1";
                Output[1].InputObject.Value = "流2";
                break;
            }
            case ScenarioMethodType.OneToMany:
            {
                for (var i = 0; i < Output.Count; i++) Output[i].InputObject.Value = $"流{i + 1}";

                break;
            }
            case ScenarioMethodType.Equal:
            {
                if (Input[1].InputObject is null)
                    Output[0].InputObject.Value = false;
                else if (Input[2].InputObject is null)
                    Output[0].InputObject.Value = false;
                else
                    Output[0].InputObject.Value = Input[1].InputObject.Value!.Equals(Input[2].InputObject.Value);

                break;
            }
            case ScenarioMethodType.VariableSet:
            {
                if (values.ContainsKey(ScenarioMethod.ValueName))
                    values[ScenarioMethod.ValueName].Value = Input[1].InputObject.Value!;

                break;
            }
            case ScenarioMethodType.VariableGet:
            {
                if (values.ContainsKey(ScenarioMethod.ValueName))
                    Output[1].InputObject.Value = values[ScenarioMethod.ValueName].Value;

                break;
            }
            case ScenarioMethodType.TempVariableSet:
            {
                if (tempValues.ContainsKey(ScenarioMethod.ValueName))
                    tempValues[ScenarioMethod.ValueName].Value = Input[1].InputObject.Value!;

                break;
            }
            case ScenarioMethodType.TempVariableGet:
            {
                if (tempValues.ContainsKey(ScenarioMethod.ValueName))
                    Output[1].InputObject.Value = tempValues[ScenarioMethod.ValueName];

                break;
            }
            case ScenarioMethodType.InputVariableGet:
            {
                if (tempValues.ContainsKey(ScenarioMethod.ValueName))
                    Output[1].InputObject.Value = inputValues[ScenarioMethod.ValueName];

                break;
            }
            case ScenarioMethodType.Condition:
            {
                if (Input[1].InputObject.Value is bool b1)
                {
                    if (b1)
                    {
                        Output[0].IsNotUsed = false;
                        Output[0].InputObject.Value = "当前流";
                        Output[1].IsNotUsed = true;
                        Output[1].InputObject.Value = "未使用的流";
                    }
                    else
                    {
                        Output[0].IsNotUsed = true;
                        Output[0].InputObject.Value = "未使用的流";
                        Output[1].IsNotUsed = false;
                        Output[1].InputObject.Value = "当前流";
                    }
                }

                break;
            }
            case ScenarioMethodType.OpenRunLocalProject:
            {
                if (Enumerable.Count<ConnectorItem>(Input) >= 3)
                {
                    List<object> parameterList = new();
                    for (var index = 2; index < Input.Count; index++) parameterList.Add(Input[index].InputObject);

                    ServiceManager.Services.GetService<ISearchItemTool>()
                        .OpenSearchItemByOnlyKey((string)Input[1].InputObject.Value,
                            parameterList.ToArray());
                }
                else
                {
                    ServiceManager.Services.GetService<ISearchItemTool>()
                        .OpenSearchItemByOnlyKey((string)Input[1].InputObject.Value);
                }

                break;
            }
            case ScenarioMethodType.Default:
            {
                if (Input == null || Input.Count == 0) break;

                var connectorItem =
                    Enumerable.First<ConnectorItem>(Input, e => e.InputObject.RealType != typeof(NodeConnectorClass));
                if (connectorItem == null) break;

                foreach (var item in Output) item.InputObject.Value = connectorItem.InputObject.Value;

                break;
            }
        }

        //将节点数据赋值给下一个节点
        foreach (var connectorItem in Output)
        {
            if (connectorItem.InputObject.RealType == typeof(NodeConnectorClass)) continue;

            foreach (var sourceOrNextConnectorItem in connectorItem.GetSourceOrNextConnectorItems(connections, false))
                sourceOrNextConnectorItem.InputObject.Value = connectorItem.InputObject.Value;
        }

        InvokeTime = DateTime.Now - start;
        return true;
    }

    public override ScenarioNodeBase Copy()
    {
        var item = new ScenarioMethodNode
        {
            Title = Title,
            ScenarioMethod = ScenarioMethod,
            Location = new Point(Location.X, Location.Y)
        };

        ObservableCollection<ConnectorItem> input = new();
        foreach (var connectorItem in Input)
            input.Add(new ConnectorItem
            {
                Anchor = new Point(connectorItem.Anchor.X, connectorItem.Anchor.Y),
                Source = item,
                Title = connectorItem.Title,
                InputObject = new CustomScenarioValue
                {
                    RealType = connectorItem.InputObject.RealType,
                    Type = connectorItem.InputObject.Type,
                    Value = connectorItem.InputObject.Value,
                    IsSelf = connectorItem.InputObject.IsSelf
                },
                AutoUnboxIndex = connectorItem.AutoUnboxIndex,
                AutoUnboxPropertyName = connectorItem.AutoUnboxPropertyName,
                SelfInputAble = connectorItem.SelfInputAble,
                ConnectorType = connectorItem.ConnectorType,
                isPluginInputConnector = connectorItem.isPluginInputConnector,
                PluginInputConnector = connectorItem.PluginInputConnector
            });

        ObservableCollection<ConnectorItem> output = new();
        foreach (var connectorItem in Output)
        {
            var connectorItem1 = new ConnectorItem
            {
                Anchor = new Point(connectorItem.Anchor.X, connectorItem.Anchor.Y),
                Source = item,
                Title = connectorItem.Title,
                InputObject = new CustomScenarioValue
                {
                    RealType = connectorItem.InputObject.RealType,
                    Type = connectorItem.InputObject.Type,
                    Value = connectorItem.InputObject.Value
                },

                AutoUnboxIndex = connectorItem.AutoUnboxIndex,
                AutoUnboxPropertyName = connectorItem.AutoUnboxPropertyName,
                IsConnected = connectorItem.IsConnected,
                ConnectorType = connectorItem.ConnectorType
            };
            if (connectorItem.Interfaces is { Count: > 0 })
            {
                List<string> interfaces = new();
                foreach (var connectorItemInterface in connectorItem.Interfaces) interfaces.Add(connectorItemInterface);

                connectorItem1.Interfaces = interfaces;
            }

            output.Add(connectorItem1);
        }

        item.Input = input;
        item.Output = output;
        return item;
    }

    public override IEnumerable<ScenarioNodeBase> GetForwardNodes(
        ObservableCollection<ConnectionItem> connections)
    {
        foreach (var connectorItem in Output)
        foreach (var sourceSource in connectorItem.GetSourceOrNextPointItems(connections, false))
            yield return sourceSource;
    }

    public override IEnumerable<ScenarioNodeBase> GetBackwardNodes(
        ObservableCollection<ConnectionItem> connections)
    {
        foreach (var connectorItem in Input)
        foreach (var sourceSource in connectorItem.GetSourceOrNextPointItems(connections, true))
            yield return sourceSource;
    }

    public override bool InputDataIsEnough(ObservableCollection<ConnectionItem> connections)
    {
        foreach (var connectorItem in Input)
            if (!connectorItem.IsConnected)
            {
                if (connectorItem.InputObject.Type.FullName != "PluginCore.NodeConnectorClass")
                {
                    //当前节点有一个输入参数不存在,验证失败
                    if (!connectorItem.InputObject.IsSelf) return false;
                }
                else
                {
                    connectorItem.IsNotUsed = true;
                }
            }
            else if (connectorItem.InputObject.Type.FullName == "PluginCore.NodeConnectorClass")
            {
                connectorItem.IsNotUsed = false;
            }

        return true;
    }

    public override bool IsUsed(ObservableCollection<ConnectionItem> connections)
    {
        var isNotUsed = Enumerable.All<ConnectorItem>(Input, connectorItem => !connectorItem.IsConnected);

        if (Enumerable.Any<ConnectorItem>(Output, connectorItem => connectorItem.IsConnected))
            isNotUsed = false;
        return !isNotUsed;
    }

    public override void ResetData()
    {
        foreach (var connectorItem in Output) connectorItem.InputObject.Value = null;

        foreach (var connectorItem in Input)
            if (!connectorItem.InputObject.IsSelf)
                connectorItem.InputObject.Value = null;

        Status = NodeStatus.Unverified;
    }

    public override void ConnectorInit()
    {
        foreach (var connectorItem in Input) ConnectorInit(connectorItem);

        foreach (var connectorItem in Output) ConnectorInit(connectorItem);
    }

    public void ConnectorInit(ConnectorItem connectorItem)
    {
        if (connectorItem.InputObject.RealType == typeof(NodeConnectorClass)) return;

        if (connectorItem.InputObject is null) return;
        if (connectorItem.isPluginInputConnector)
        {
            var instance = Activator.CreateInstance(connectorItem.InputObject.Type);
            instance.GetType().GetProperty("Value").SetValue(instance, new ObservableValue
            {
                Value = new CustomScenarioValue
                {
                    Type = connectorItem.InputObject.Type,
                    RealType = connectorItem.InputObject.RealType,
                    Value = connectorItem.InputObject.Value
                }
            });
            connectorItem.PluginInputConnector = instance as INodeInputConnector;
            return;
        }
    }

    public override bool IsUseThePlugin(string plugStr)
    {
        var pluginManger = ServiceManager.Services.GetService<IPluginManger>()!;
        return ScenarioMethod.PluginInfo?.ToPlgString() == plugStr ||
               Enumerable.Any<ConnectorItem>(Input, e =>
                   pluginManger.IsTypeFromThePlugin(e.InputObject?.RealType, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.InputObject?.Type, plugStr)) ||
               Enumerable.Any<ConnectorItem>(Output, e =>
                   pluginManger.IsTypeFromThePlugin(e.InputObject?.RealType, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.InputObject?.Type, plugStr));
    }
}