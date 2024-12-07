#region

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.SDKs.CustomScenario;

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
        var canConnect = value != null;
        switch (value)
        {
            case ConnectorItem con:
            {
                if (con == Source || con.Source == Source.Source)
                {
                    PreviewText = $"不能自己连接自己";
                    break;
                }

                if (Source.IsOut == con.IsOut)
                {
                    PreviewText = $"错误的连接";
                    break;
                }

                if (Source.InputObject.Type.FullName != con.InputObject.Type.FullName)
                {
                    if (con.InputObject.Type.FullName == "System.Object")
                    {
                        PreviewText = "连接";
                        break;
                    }

                    if (Source.InputObject.Type.FullName == "System.Object")
                    {
                        PreviewText = "连接";
                        break;
                    }

                    if (con.InputObject.Type.IsAssignableFrom(Source.InputObject.Type))
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
                PreviewText = $"丢弃连接";
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
        if (target == null) return;

        if (target == Source || target.Source == Source.Source) return;

        if (Source.InputObject.Type.FullName != target.InputObject.Type.FullName &&
            !(target.InputObject.Type.IsAssignableFrom(Source.InputObject.Type) ||
              Source.InputObject.Type.FullName == "System.Object" ||
              target.InputObject.Type.FullName == "System.Object"))
            return;

        if (Source.IsOut != target.IsOut)
        {
            if (!Source.IsOut)
                _editor.Connect(target, Source);
            else
                _editor.Connect(Source, target);
        }
    }
}