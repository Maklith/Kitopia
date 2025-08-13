using System.Text.Json.Serialization;
using System.Windows.Input;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.ViewModel.TaskEditor;

namespace Core.SDKs.CustomScenario;

public partial class ConnectionItem
{
    [JsonIgnore]
    public ICommand SplitConnectionCommand { get; init; }
    public ConnectionItem(ConnectorItem source, ConnectorItem target)
    {
        Source = source;
        Target = target;
        
        Source.IsConnected = true;
        Target.IsConnected = true;
        SplitConnectionCommand = new RelayCommand<Point>((e) =>
        {
            
        });
    }

    public ConnectionItem()
    {
    }

    public ConnectorItem Source { get; set; }

    public ConnectorItem Target { get; set; }
    
    
}