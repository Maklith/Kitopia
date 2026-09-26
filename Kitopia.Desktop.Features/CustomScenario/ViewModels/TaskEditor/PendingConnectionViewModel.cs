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
                    PreviewText = "不能自己连接自己";
                    break;
                }

                var reverse = Source.ConnectorType == ConnectorType.Input || con.ConnectorType == ConnectorType.Output;
                var source = reverse ? con : Source;
                var target = reverse ? Source : con;
                if (ScenarioGraph.WouldCreateCycle(_editor.Scenario.Connections, source, target))
                {
                    PreviewText = "连接会形成循环";
                    break;
                }

                if (Source.ConnectorType != ConnectorType.Both && Source.ConnectorType == con.ConnectorType)
                {
                    PreviewText = "错误的连接";
                    break;
                }

                PreviewText = ScenarioGraph.CanConnect(source, target) ? "连接" : "类型错误";

                break;
            }
            default:
                PreviewText = "选择节点";
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
