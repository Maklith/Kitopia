#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Core.CustomScenario;
using ConnectorItem = Core.CustomScenario.ConnectorItem;

#endregion

namespace Core.ViewModel.TaskEditor;

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
                    PreviewText = $"不能自己连接自己";
                    break;
                }

                if (Source.ConnectorType != ConnectorType.Both && Source.ConnectorType == con.ConnectorType)
                {
                    PreviewText = $"错误的连接";
                    break;
                }

                if (Source.InputObject.RealType.FullName != con.InputObject.RealType.FullName)
                {
                    if (con.InputObject.RealType.FullName == "System.Object")
                    {
                        PreviewText = "连接";
                        break;
                    }

                    if (Source.InputObject.RealType.FullName == "System.Object")
                    {
                        PreviewText = "连接";
                        break;
                    }

                    if (con.InputObject.RealType.IsAssignableFrom(Source.InputObject.RealType))
                    {
                        PreviewText = "连接";
                        break;
                    }

                    PreviewText = $"类型错误";
                    break;
                }

                PreviewText = "连接";

                break;
            }
            default:
                PreviewText = $"选择节点";
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

        if (target == Source || target.Source == Source.Source) return;

        if (Source.InputObject?.RealType.FullName != target.InputObject?.RealType.FullName &&
            target.InputObject != null &&
            !(target.InputObject.RealType.IsAssignableFrom(Source.InputObject?.RealType) ||
              Source.InputObject?.RealType.FullName == "System.Object" ||
              target.InputObject?.RealType.FullName == "System.Object"))
            return;

        if (Source.ConnectorType != ConnectorType.Both && Source.ConnectorType == target.ConnectorType) return;

        switch (Source.ConnectorType)
        {
            case ConnectorType.Input:
                _editor.Connect(target, Source);
                break;
            case ConnectorType.Output:
                _editor.Connect(Source, target);
                break;
            case ConnectorType.Both:
                _editor.Connect(Source, target);
                break;
            case ConnectorType.Custom:
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}

public record RequestNodeSearchMessage(ConnectorItem Source);