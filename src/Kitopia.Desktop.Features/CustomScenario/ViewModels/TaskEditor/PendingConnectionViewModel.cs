using Kitopia.Feature.Localization;
#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

#endregion

namespace Kitopia.Desktop.Features.CustomScenario.ViewModels.TaskEditor;

public partial class PendingConnectionViewModel : ObservableRecipient
{
    private readonly TaskEditorViewModel _editor;

    [ObservableProperty] private object? _previewTarget;
    [ObservableProperty] private string _previewText;
    [ObservableProperty] private ConnectorItem _source;

    public PendingConnectionViewModel(TaskEditorViewModel editor)
    {
        _editor = editor;
    }

    partial void OnPreviewTargetChanged(object? value)
    {
        switch (value)
        {
            case ConnectorItem con:
            {
                if (con == Source || con.Source == Source.Source)
                {
                    PreviewText = Lang.Get("lang.kitopia.cannot_connect_a_node_to_itself");
                    break;
                }

                var reverse = Source.ConnectorType == ConnectorType.Input || con.ConnectorType == ConnectorType.Output;
                var source = reverse ? con : Source;
                var target = reverse ? Source : con;
                if (ScenarioGraph.WouldCreateCycle(_editor.Scenario.Connections, source, target))
                {
                    PreviewText = Lang.Get("lang.kitopia.connection_would_create_a_cycle");
                    break;
                }

                if (Source.ConnectorType != ConnectorType.Both && Source.ConnectorType == con.ConnectorType)
                {
                    PreviewText = Lang.Get("lang.kitopia.invalid_connection");
                    break;
                }

                PreviewText = ScenarioGraph.CanConnect(source, target) ? Lang.Get("lang.kitopia.connect") : Lang.Get("lang.kitopia.type_mismatch");

                break;
            }
            default:
                PreviewText = Lang.Get("lang.kitopia.select_a_node");
                break;
        }
    }

    [RelayCommand]
    public void Start(ConnectorItem item)
    {
        Source = item;
    }

    [RelayCommand]
    public void Finish(ConnectorItem? target)
    {
        if (target == null)
        {
            WeakReferenceMessenger.Default.Send(new RequestNodeSearchMessage(Source));
            return;
        }

        _editor.Connect(Source, target);
    }
}

public record RequestNodeSearchMessage(ConnectorItem Source);
