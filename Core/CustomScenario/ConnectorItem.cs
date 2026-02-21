using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.JsonConverter;
using PluginCore.CustomScenario;

namespace Core.CustomScenario;

/// <summary>
/// 连接器类型枚举，定义连接器的输入输出属性
/// Connector type enumeration that defines input/output properties of connectors
/// </summary>
public enum ConnectorType
{
    /// <summary>输入连接器 / Input connector</summary>
    Input,
    
    /// <summary>输出连接器 / Output connector</summary>
    Output,
    
    /// <summary>双向连接器 / Bidirectional connector</summary>
    Both,
    
    /// <summary>自定义连接器 / Custom connector</summary>
    Custom
}

/// <summary>
/// 连接器项，表示场景节点的输入输出连接点
/// Connector item representing input/output connection points of scenario nodes
/// </summary>
public partial class ConnectorItem : ObservableRecipient
{
    [property: JsonConverter(typeof(PointJsonConverter))]
    [ObservableProperty]
    private Point _anchor;

    /// <summary>
    /// 输入对象数据 / Input object data
    /// </summary>
    [JsonConverter(typeof(CustomScenarioInputValueJsonConverter))]
    public CustomScenarioValue? InputObject { get; init; }

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isNotUsed;
    [ObservableProperty] private ConnectorType _connectorType = ConnectorType.Input;

    /// <summary>
    /// 是否允许自身输入 / Whether self-input is allowed
    /// </summary>
    public bool SelfInputAble { get; set; } = true;

    /// <summary>
    /// 自动拆箱索引 / Auto unbox index
    /// </summary>
    public int AutoUnboxIndex { get; set; }
    
    /// <summary>
    /// 自动拆箱属性名称 / Auto unbox property name
    /// </summary>
    public string AutoUnboxPropertyName { get; set; } = string.Empty;

    /// <summary>
    /// 连接器标题 / Connector title
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// 支持的接口列表 / List of supported interfaces
    /// </summary>
    [JsonIgnore]
    public List<string>? Interfaces
    {
        get
        {
            if (InputObject == null) return null;
            var type = InputObject.ShowType;
            
            if (type.FullName == null || type.FullName.StartsWith("System.")) return null;
            List<string> interfaces = new();
            foreach (var @interface in type.GetInterfaces())
                interfaces.Add(@interface.FullName);
            return interfaces;
        }
    }

    /// <summary>
    /// 连接器所属的源节点 / Source node that owns this connector
    /// </summary>
    public ScenarioNodeBase Source { get; set; }

    /// <summary>
    /// 获取源连接器或下一个连接器项
    /// Get source connector items or next connector items
    /// </summary>
    /// <param name="connectionItems">连接项集合 / Collection of connection items</param>
    /// <param name="source">是否获取源连接器 / Whether to get source connectors</param>
    /// <returns>连接器项集合 / Collection of connector items</returns>
    public IEnumerable<ConnectorItem> GetSourceOrNextConnectorItems(
        ObservableCollection<ConnectionItem> connectionItems, bool source)
    {
        if (!source)
            return connectionItems.Where(e => e.Source == this)
                .Select(e => e.Target);

        return connectionItems.Where(e => e.Target == this)
            .Select(e => e.Source);
    }

    /// <summary>
    /// 获取源节点或下一个节点项
    /// Get source node items or next node items
    /// </summary>
    /// <param name="connectionItems">连接项集合 / Collection of connection items</param>
    /// <param name="source">是否获取源节点 / Whether to get source nodes</param>
    /// <returns>节点集合 / Collection of scenario nodes</returns>
    public IEnumerable<ScenarioNodeBase> GetSourceOrNextPointItems(
        ObservableCollection<ConnectionItem> connectionItems, bool source)
    {
        if (!source)
            return connectionItems.Where(e => e.Source == this)
                .Select(e => e.Target.Source);

        return connectionItems.Where(e => e.Target == this)
            .Select(e => e.Source.Source);
    }

    /// <summary>
    /// 是否为插件自定义输入连接器 / Whether this is a plugin custom input connector
    /// </summary>
    public bool isPluginInputConnector { get; set; }
    
    /// <summary>
    /// 插件输入连接器实例 / Plugin input connector instance
    /// </summary>
    [JsonIgnore] public INodeInputConnector PluginInputConnector { get; set; }
}