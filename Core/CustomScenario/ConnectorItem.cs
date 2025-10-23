using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.Infrastructure.JsonConverter;
using PluginCore;

namespace Core.CustomScenario;

public enum ConnectorType
{
    Input,
    Output,
    Both,
    Custom
}

public partial class ConnectorItem : ObservableRecipient
{
    [property: JsonConverter(typeof(PointJsonConverter))]
    [ObservableProperty]
    private Point _anchor;

    [JsonConverter(typeof(CustomScenarioInputValueJsonConverter))]
    public CustomScenarioValue? InputObject { get; init; } //数据

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isNotUsed = false;
    [ObservableProperty] private ConnectorType _connectorType = ConnectorType.Input;


    public bool SelfInputAble { get; set; } = true;

    public int AutoUnboxIndex { get; set; }
    public string AutoUnboxPropertyName { get; set; } = string.Empty;


    public string Title { get; set; }


    public List<string>? Interfaces { get; set; }

    public ScenarioNodeBase Source { get; set; }

    public IEnumerable<ConnectorItem> GetSourceOrNextConnectorItems(
        ObservableCollection<ConnectionItem> connectionItems, bool source)
    {
        if (!source)
            return connectionItems.Where((e) => e.Source == this)
                .Select(e => e.Target);

        return connectionItems.Where((e) => e.Target == this)
            .Select(e => e.Source);
    }

    public IEnumerable<ScenarioNodeBase> GetSourceOrNextPointItems(
        ObservableCollection<ConnectionItem> connectionItems, bool source)
    {
        if (!source)
            return connectionItems.Where((e) => e.Source == this)
                .Select(e => e.Target.Source);

        return connectionItems.Where((e) => e.Target == this)
            .Select(e => e.Source.Source);
    }

    //插件自定义输入连接器
    public bool isPluginInputConnector { get; set; }
    [JsonIgnore] public INodeInputConnector PluginInputConnector { get; set; }
}