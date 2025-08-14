#region

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Core.CustomScenario;
using Core.ViewModel.TaskEditor;
using NodifyM.Avalonia.Events;
using PluginCore;
using Ursa.Controls;
using DataObject = Avalonia.Input.DataObject;
using DragDrop = Avalonia.Input.DragDrop;
using DragDropEffects = Avalonia.Input.DragDropEffects;
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


    private void ListBox_OnMouseMove(object? sender, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(this);

        if (sender is Border border)
            if (point.Properties.IsLeftButtonPressed)
            {
                var borderDataContext = border.DataContext;
                try
                {
                    ScenarioMethodNode pointItem = null!;
                    switch ((string)border.Tag)
                    {
                        case "Node":
                        {
                            pointItem = (ScenarioMethodNode)border.DataContext;
                            break;
                        }
                        case "Set":
                        {
                            var keyValuePair = (KeyValuePair<string, CustomScenarioValue>)borderDataContext;
                            pointItem = new ScenarioMethod(ScenarioMethodType.变量设置)
                                    { ValueName = keyValuePair.Key, ValueDataType = keyValuePair.Value.Type }
                                .GenerateNode();
                            break;
                        }
                        case "Get":
                        {
                            var keyValuePair = (KeyValuePair<string, CustomScenarioValue>)borderDataContext;
                            pointItem = new ScenarioMethod(ScenarioMethodType.变量获取)
                                    { ValueName = keyValuePair.Key, ValueDataType = keyValuePair.Value.Type }
                                .GenerateNode();
                            break;
                        }
                        case "TempSet":
                        {
                            var keyValuePair = (KeyValuePair<string, object>)borderDataContext;
                            pointItem = new ScenarioMethod(ScenarioMethodType.临时变量设置)
                                    { ValueName = keyValuePair.Key }
                                .GenerateNode();
                            break;
                        }
                        case "TempGet":
                        {
                            var keyValuePair = (KeyValuePair<string, object>)borderDataContext;
                            pointItem = new ScenarioMethod(ScenarioMethodType.临时变量获取)
                                    { ValueName = keyValuePair.Key }
                                .GenerateNode();
                            break;
                        }
                    }

                    var data = new DataObject();
                    data.Set("KitopiaPointItem", pointItem);
                    DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
                    var renderTargetBitmap =
                        new RenderTargetBitmap(new PixelSize((int)border.Bounds.Width, (int)border.Bounds.Height));
                    renderTargetBitmap.Render(border);
                    //Cursor.Dispose();
                    //Cursor = new Cursor(renderTargetBitmap,new PixelPoint((int)(renderTargetBitmap.Size.Width/2),(int)(renderTargetBitmap.Size.Height/2)));
                }
                catch (Exception exception)
                {
                    Console.WriteLine(exception);
                }
            }
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