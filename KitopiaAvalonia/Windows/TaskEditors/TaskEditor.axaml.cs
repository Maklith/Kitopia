#region

using Avalonia.Controls.Primitives;
using Core.CustomScenario;
using Core.ViewModel.TaskEditor;
using NodifyM.Avalonia.Events;
using Ursa.Controls;
using DragDrop = Avalonia.Input.DragDrop;
using DragEventArgs = Avalonia.Input.DragEventArgs;
using Point = Avalonia.Point;

#endregion

namespace KitopiaAvalonia.Windows;

public partial class TaskEditor : UrsaWindow
{
    public TaskEditor()
    {
        InitializeComponent();
        Editor.AddHandler(DragDrop.DropEvent, NodifyEditor_Drop);
    }

    public void LoadTask(CustomScenario name)
    {
        ((TaskEditorViewModel)DataContext).Load(name);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        //((TaskEditorViewModel)DataContext).ContentPresenter = ContentPresenter;
    }


    private void NodifyEditor_Drop(object sender, DragEventArgs e)
    {
        //throw new System.NotImplementedException();
        if (e.Data.Get("KitopiaPointItem") is ScenarioMethodNode fromListNode)
        {
            var command = add.Command;
            if (command != null &&
                command.CanExecute(fromListNode)) // Check if the command is not null and can be executed
            {
                var point = e.GetPosition(Editor);
                point -= new Point(Editor.ViewTranslateTransform.X, Editor.ViewTranslateTransform.Y);
                fromListNode.Location = point;
                command.Execute(fromListNode); // Pass null or any other parameter as needed
            }
        }
    }

    private void NodifyEditor_DragOver(object sender, DragEventArgs e)
    {
        //throw new System.NotImplementedException();
    }

    private void NodifyEditor_DragEnter(object sender, DragEventArgs e)
    {
        //throw new System.NotImplementedException();
    }

    private void NodifyEditor_DragLeave(object sender, DragEventArgs e)
    {
        //throw new System.NotImplementedException();
    }

    public void BaseConnection_OnSplit(object sender, ConnectionEventArgs e)
    {
        if (e.Connection is ConnectionItem connection)
        {
            var vm = (TaskEditorViewModel)DataContext;
            vm.SplitConnection(connection, e.SplitLocation);
        }
    }
}