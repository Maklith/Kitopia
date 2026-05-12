#region

using System;
using Avalonia;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Messaging;
using Core.CustomScenario;
using Core.ViewModel.TaskEditor;
using NodifyM.Avalonia.Events;
using Ursa.Controls;
using DragDrop = Avalonia.Input.DragDrop;
using DragEventArgs = Avalonia.Input.DragEventArgs;
using Point = Avalonia.Point;

#endregion

namespace KitopiaAvalonia.Windows.TaskEditors;

public partial class TaskEditor : UrsaWindow
{
    private Point _lastMousePosition;

    public TaskEditor()
    {
        InitializeComponent();
        Editor.AddHandler(DragDrop.DropEvent, NodifyEditor_Drop);
        
        // Track pointer position more reliably using Tunneling and handledEventsToo: true
        this.AddHandler(PointerMovedEvent, (sender, args) =>
        {
            _lastMousePosition = args.GetPosition(Editor);
        }, RoutingStrategies.Tunnel, true);
        
        WeakReferenceMessenger.Default.Register<RequestNodeSearchMessage>(this, OnNodeSearchRequested);
    }

    private void OnNodeSearchRequested(object recipient, RequestNodeSearchMessage message)
    {
        var point = _lastMousePosition;
        // Calculate canvas location: (LocalPoint - Offset)
        var canvasLocation = point - new Point(Editor.OffsetX, Editor.OffsetY);

        var searchWin = new TaskNodeSearchWindow();
        var vm = new TaskNodeSearchViewModel(message.Source, (TaskEditorViewModel)DataContext, canvasLocation);
        searchWin.DataContext = vm;
        
        var visualEditor = (Visual)Editor;
        var screenPos = visualEditor.PointToScreen(point);
        searchWin.Position = screenPos;
        
        searchWin.Show(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Editor.RemoveHandler(DragDrop.DropEvent, NodifyEditor_Drop);
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    public void LoadTask(CustomScenario name)
    {
        ((TaskEditorViewModel)DataContext).Load(name);
    }

    private void NodifyEditor_Drop(object sender, DragEventArgs e)
    {
        if (TaskEditorViewModel.CurrentDragPayload is ScenarioMethodNode fromListNode)
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
