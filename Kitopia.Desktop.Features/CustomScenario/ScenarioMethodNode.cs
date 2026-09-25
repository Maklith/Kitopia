using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Kitopia.Desktop.Features.JsonConverter;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using NodifyM.Avalonia.ViewModelBase;
using PluginCore;
using PluginCore.CustomScenario;
using PluginCore.CustomScenario.Attribute.Scenario;

namespace Kitopia.Desktop.Features.CustomScenario;

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
public partial class ScenarioNodeBase : ObservableRecipient,INodePosition
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

    public virtual ValueTask<bool> InvokeAsync(CancellationToken cancellationToken,
        ObservableCollection<ConnectionItem> connections,
        ObservableDictionary<string, CustomScenarioValue> values,
        ObservableDictionary<string, CustomScenarioValue> tempValues,
        ObservableDictionary<string, CustomScenarioValue> inputValues)
    {
        return new ValueTask<bool>(Invoke(cancellationToken, connections, values, tempValues, inputValues));
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
    [ObservableProperty] private ConnectorItem connector;

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
            Title = Title,
            InputObject = new CustomScenarioValue
            {
                ShowType = Connector.InputObject.ShowType,
                SerializeType = Connector.InputObject.SerializeType,
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
        return InvokeAsync(cancellationToken, connections, values, tempValues, inputValues)
            .GetAwaiter()
            .GetResult();
    }

    public override async ValueTask<bool> InvokeAsync(CancellationToken cancellationToken,
        ObservableCollection<ConnectionItem> connections,
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
                List<object?> list = new();
                var index = 1;
                foreach (var parameterInfo in ScenarioMethod.Method.GetParameters())
                {
                    if (parameterInfo.ParameterType == typeof(CancellationToken) ||
                        Nullable.GetUnderlyingType(parameterInfo.ParameterType) == typeof(CancellationToken))
                    {
                        list.Add(cancellationToken);
                        continue;
                    }

                    if (parameterInfo.ParameterType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                    {
                        if (index >= Input.Count) return false;
                        var autoUnboxIndex = Input[index].AutoUnboxIndex;
                        var instance = parameterInfo.ParameterType.GetConstructor([])
                            ?.Invoke([]);
                        if (instance is null)
                            return false;

                        while (Input.Count > index && Input[index].AutoUnboxIndex == autoUnboxIndex)
                        {
                            var item = Input[index].InputObject;
                            var property = parameterInfo.ParameterType.GetProperty(Input[index].AutoUnboxPropertyName);
                            if (property is null) return false;
                            property.SetValue(instance, item.Value);
                            index++;
                        }

                        list.Add(instance);
                        continue;
                    }

                    if (index >= Input.Count)
                    {
                        if (!parameterInfo.HasDefaultValue) return false;
                        list.Add(parameterInfo.DefaultValue);
                        continue;
                    }

                    var input = Input[index++];
                    if (input.IsPluginInputConnector)
                    {
                        list.Add(input.InputObject.Value);
                    }
                    else
                    {
                        var inputObject = input.InputObject.Value;
                        if (inputObject is not null)
                            list.Add(inputObject);
                        else if (input.InputObject.IsSelf &&
                                 (!parameterInfo.ParameterType.IsValueType ||
                                  Nullable.GetUnderlyingType(parameterInfo.ParameterType) is not null))
                            list.Add(null);
                        else if (parameterInfo.HasDefaultValue)
                            list.Add(parameterInfo.DefaultValue);
                        else
                            return false;
                    }
                }

                var target = ScenarioMethod.Method.IsStatic
                    ? null
                    : ScenarioMethod.ServiceProvider!.GetService(ScenarioMethod.Method.DeclaringType!) ??
                      throw new InvalidOperationException(
                          $"未注册插件情景方法类型 {ScenarioMethod.Method.DeclaringType!.FullName}");
                var invoke = ScenarioMethod.Method.Invoke(target, list.ToArray());

                var hasReturnValue = ScenarioMethod.TryGetReturnValueType(
                    ScenarioMethod.Method.ReturnParameter.ParameterType, out var returnValueType);
                var result = invoke is null && !hasReturnValue
                    ? null
                    : await AwaitInvocationResultAsync(invoke).ConfigureAwait(false);

                if (hasReturnValue && returnValueType.GetCustomAttribute(typeof(AutoUnbox)) is not null)
                {
                    if (result is null)
                        return false;

                    foreach (var connectorItem in Output.Where(item =>
                                 !string.IsNullOrWhiteSpace(item.AutoUnboxPropertyName)))
                    {
                        var property = returnValueType.GetProperty(connectorItem.AutoUnboxPropertyName!,
                            BindingFlags.Instance | BindingFlags.IgnoreCase |
                            BindingFlags.Public | BindingFlags.NonPublic);
                        if (property is null) return false;
                        connectorItem.InputObject.Value = property.GetValue(result);
                    }
                }
                else if (hasReturnValue)
                {
                    if (Output.Count<ConnectorItem>() >= 2) Output[1].InputObject.Value = result;
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
                    Output[1].InputObject.Value = tempValues[ScenarioMethod.ValueName].Value;

                break;
            }
            case ScenarioMethodType.InputVariableGet:
            {
                if (inputValues.TryGetValue(ScenarioMethod.ValueName, out var input))
                    Output[1].InputObject.Value = input.Value;

                break;
            }
            case ScenarioMethodType.Condition:
            {
                if (Input[1].InputObject.Value is not bool condition) return false;
                Output[0].IsNotUsed = !condition;
                Output[0].InputObject.Value = condition ? "当前流" : "未使用的流";
                Output[1].IsNotUsed = condition;
                Output[1].InputObject.Value = condition ? "未使用的流" : "当前流";

                break;
            }
            case ScenarioMethodType.OpenRunLocalProject:
            {
                if (Input.Count<ConnectorItem>() >= 3)
                {
                    List<object> parameterList = new();
                    for (var index = 2; index < Input.Count; index++)
                        parameterList.Add(Input[index].InputObject.Value!);

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
                    Input.FirstOrDefault(e => e.InputObject.ShowType != typeof(NodeConnectorClass));
                if (connectorItem == null) break;

                foreach (var item in Output) item.InputObject.Value = connectorItem.InputObject.Value;

                break;
            }
        }

        //将节点数据赋值给下一个节点
        foreach (var connectorItem in Output)
        {
            if (connectorItem.InputObject.ShowType == typeof(NodeConnectorClass)) continue;

            foreach (var sourceOrNextConnectorItem in connectorItem.GetSourceOrNextConnectorItems(connections, false))
                sourceOrNextConnectorItem.InputObject.Value = connectorItem.InputObject.Value;
        }

        InvokeTime = DateTime.Now - start;
        return true;
    }

    private static async ValueTask<object?> AwaitInvocationResultAsync(object? invocationResult)
    {
        if (invocationResult is null)
            return null;

        if (invocationResult is Task task)
        {
            await task.ConfigureAwait(false);
            return GetTaskResult(task);
        }

        if (invocationResult is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return null;
        }

        var invocationType = invocationResult.GetType();
        if (invocationType.IsGenericType &&
            invocationType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var asTask = (Task)invocationType.GetMethod(nameof(ValueTask<int>.AsTask))!
                .Invoke(invocationResult, null)!;
            await asTask.ConfigureAwait(false);
            return GetTaskResult(asTask);
        }

        return invocationResult;
    }

    private static object? GetTaskResult(Task task)
    {
        return task.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(task);
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
                    ShowType = connectorItem.InputObject.ShowType,
                    SerializeType = connectorItem.InputObject.SerializeType,
                    Value = connectorItem.InputObject.Value,
                    IsSelf = connectorItem.InputObject.IsSelf
                },
                AutoUnboxIndex = connectorItem.AutoUnboxIndex,
                AutoUnboxPropertyName = connectorItem.AutoUnboxPropertyName,
                OnlySelfInput = connectorItem.OnlySelfInput,
                ConnectorType = connectorItem.ConnectorType,
                IsPluginInputConnector = connectorItem.IsPluginInputConnector,
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
                    ShowType = connectorItem.InputObject.ShowType,
                    SerializeType = connectorItem.InputObject.SerializeType,
                    Value = connectorItem.InputObject.Value
                },

                AutoUnboxIndex = connectorItem.AutoUnboxIndex,
                AutoUnboxPropertyName = connectorItem.AutoUnboxPropertyName,
                IsConnected = connectorItem.IsConnected,
                ConnectorType = connectorItem.ConnectorType
            };
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
                if (connectorItem.InputObject.SerializeType.FullName != typeof(NodeConnectorClass).FullName!)
                {
                    //当前节点有一个输入参数不存在,验证失败
                    if (!connectorItem.InputObject.IsSelf) return false;
                }
                else
                {
                    connectorItem.IsNotUsed = true;
                }
            }
            else if (connectorItem.InputObject.SerializeType.FullName == typeof(NodeConnectorClass).FullName!)
            {
                connectorItem.IsNotUsed = false;
            }

        return true;
    }

    public override bool IsUsed(ObservableCollection<ConnectionItem> connections)
    {
        var isNotUsed = Input.All<ConnectorItem>(connectorItem => !connectorItem.IsConnected);

        if (Output.Any<ConnectorItem>(connectorItem => connectorItem.IsConnected))
            isNotUsed = false;
        return !isNotUsed;
    }

    public override void ResetData()
    {
        foreach (var connectorItem in Output) {
            connectorItem.InputObject.Value = null;
            connectorItem.IsNotUsed = false;
        }

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
        if (connectorItem.InputObject is null || connectorItem.InputObject.ShowType == typeof(NodeConnectorClass)) return;
        if (connectorItem.IsPluginInputConnector)
        {
            var instance = Activator.CreateInstance(connectorItem.InputObject.ShowType);
            var valueProperty = instance?.GetType().GetProperty("Value");
            if (instance is null || valueProperty is null || !valueProperty.CanWrite) return;
            valueProperty.SetValue(instance, new ObservableValue
            {
                Value = new CustomScenarioValue
                {
                    SerializeType = connectorItem.InputObject.SerializeType,
                    ShowType = connectorItem.InputObject.ShowType,
                    Value = connectorItem.InputObject.Value
                }
            });
            connectorItem.PluginInputConnector = instance as INodeInputConnector;
        }
    }

    public override bool IsUseThePlugin(string plugStr)
    {
        var pluginManger = ServiceManager.Services.GetService<IPluginManger>()!;
        return ScenarioMethod.PluginInfo?.ToPlgString() == plugStr ||
               Input.Any<ConnectorItem>(e =>
                   pluginManger.IsTypeFromThePlugin(e.InputObject.ShowType, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.InputObject.SerializeType, plugStr)) ||
               Output.Any<ConnectorItem>(e =>
                   pluginManger.IsTypeFromThePlugin(e.InputObject.ShowType, plugStr) ||
                   pluginManger.IsTypeFromThePlugin(e.InputObject.SerializeType, plugStr));
    }
}
