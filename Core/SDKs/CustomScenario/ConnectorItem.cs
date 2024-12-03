using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Core.JsonConverter;
using Core.SDKs.Services.Config;
using PluginCore;

namespace Core.SDKs.CustomScenario;

public partial class ConnectorItem : ObservableRecipient, IConnectorItem
{
    [JsonConverter(typeof(PointJsonConverter))]
    #pragma warning disable CS0657 // 不是此声明的有效特性位置
    [property: JsonConverter(typeof(PointJsonConverter))]
    #pragma warning restore CS0657 // 不是此声明的有效特性位置
    [ObservableProperty]
    private Point _anchor;

    #pragma warning disable CS0657 // Not a valid attribute location for this declaration
    [property: JsonIgnore]
    [JsonIgnore]
    #pragma warning restore CS0657 // Not a valid attribute location for this declaration
    [ObservableProperty]
    private object? _inputObject; //数据

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isNotUsed = false;
    [ObservableProperty] private bool _isOut;
    [ObservableProperty] private bool _isSelf = false;

    [JsonIgnore] private Type? _realType;

    public bool SelfInputAble { get; set; } = true;

    public int AutoUnboxIndex { get; set; }

    public string TypeName { get; set; }

    public string Title { get; set; }

    /// <summary>
    /// 输出的类型
    /// </summary>
    [JsonConverter(typeof(TypeJsonConverter))]
    public Type Type { get; set; }

    [JsonConverter(typeof(TypeJsonConverter))]
    public Type RealType
    {
        get => _realType ?? Type;
        set => _realType = value;
    }

    public List<string>? Interfaces { get; set; }

    public ScenarioMethodNode Source { get; set; }

    public IEnumerable<ConnectorItem> GetSourceOrNextConnectorItems(
        ObservableCollection<ConnectionItem> connectionItems)
    {
        if (IsOut)
        {
            return connectionItems.Where((e) => e.Source == this)
                .Select(e => e.Target);
        }

        return connectionItems.Where((e) => e.Target == this)
            .Select(e => e.Source);
    }

    public IEnumerable<ScenarioMethodNode> GetSourceOrNextPointItems(
        ObservableCollection<ConnectionItem> connectionItems)
    {
        if (IsOut)
        {
            return connectionItems.Where((e) => e.Source == this)
                .Select(e => e.Target.Source);
        }

        return connectionItems.Where((e) => e.Target == this)
            .Select(e => e.Source.Source);
    }

    partial void OnInputObjectChanged(object? value)
    {
        WeakReferenceMessenger.Default.Send(new CustomScenarioChangeMsg()
            { ScenarioMethodNode = Source, ConnectorItem = this });
    }

    //插件自定义输入连接器
    public bool isPluginInputConnector { get; set; }
    public INodeInputConnector PluginInputConnector { get; set; }
}