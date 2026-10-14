using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.Input;

namespace Kitopia.Desktop.Features.CustomScenario;

public class ConnectionItem
{
    private readonly ConnectorItem _source;
    private readonly ConnectorItem _target;
    [JsonIgnore] public ICommand? SplitConnectionCommand { get; set; }

    public required ConnectorItem Source
    {
        get => _source;
        [MemberNotNull(nameof(_source))]
        init
        {
            _source = value;
            _source.IsConnected = true;
        }
    }

    public required ConnectorItem Target
    {
        get => _target;
        [MemberNotNull(nameof(_target))]
        init
        {
            _target = value;
            _target.IsConnected = true;
        }
    }

    public void Init(Action<ConnectionItem, Point> splitAction)
    {
        if (SplitConnectionCommand != null) return;
        SplitConnectionCommand = new RelayCommand<Point>(point => { splitAction(this, point); });
    }
}