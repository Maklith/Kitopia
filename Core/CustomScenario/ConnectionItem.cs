using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.Input;

namespace Core.CustomScenario;

public partial class ConnectionItem
{
    [JsonIgnore] public ICommand? SplitConnectionCommand { get; set; }

    public ConnectionItem(ConnectorItem source, ConnectorItem target)
    {
        Source = source;
        Target = target;

        Source.IsConnected = true;
        Target.IsConnected = true;
    }

    public ConnectionItem()
    {
    }

    public ConnectorItem Source { get; set; }

    public ConnectorItem Target { get; set; }

    public void Init(Action<ConnectionItem, Point> splitAction)
    {
        if (SplitConnectionCommand != null) return;
        SplitConnectionCommand = new RelayCommand<Point>((point) => { splitAction(this, point); });
    }
}