// Author: liaom
// SolutionName: Kitopia
// ProjectName: KitopiaAvalonia
// FileName:TaskEditorValueToScenarioMethodNode.axaml.cs
// Date: 2025/09/17 14:09
// FileEffect:

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Core.CustomScenario;
using PluginCore;

namespace KitopiaAvalonia.Windows.TaskEditors;

public partial class TaskEditorValueToScenarioMethodNode : UserControl
{
    public static readonly StyledProperty<KeyValuePair<string, CustomScenarioValue>> SourceProperty =
        AvaloniaProperty.Register<TaskEditorValueToScenarioMethodNode, KeyValuePair<string, CustomScenarioValue>>(
            nameof(Source));

    public static readonly StyledProperty<ScenarioNodeBase> NodeProperty =
        AvaloniaProperty.Register<TaskEditorValueToScenarioMethodNode, ScenarioNodeBase>(
            nameof(Node));

    public static readonly StyledProperty<ValueType> ValueTypeProperty =
        AvaloniaProperty.Register<TaskEditorValueToScenarioMethodNode, ValueType>(
            nameof(ValueType), inherits: true);

    public static readonly StyledProperty<ValueForward> ValueForwardProperty =
        AvaloniaProperty.Register<TaskEditorValueToScenarioMethodNode, ValueForward>(
            nameof(ValueForward));

    static TaskEditorValueToScenarioMethodNode()
    {
        ValueTypeProperty.Changed.AddClassHandler<TaskEditorValueToScenarioMethodNode>(Action);
        SourceProperty.Changed.AddClassHandler<TaskEditorValueToScenarioMethodNode>(Action);
    }

    public TaskEditorValueToScenarioMethodNode()
    {
        InitializeComponent();
    }

    public ValueForward ValueForward
    {
        get => GetValue(ValueForwardProperty);
        set => SetValue(ValueForwardProperty, value);
    }

    public ValueType ValueType
    {
        get => GetValue(ValueTypeProperty);
        set => SetValue(ValueTypeProperty, value);
    }

    public ScenarioNodeBase Node
    {
        get => GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }


    public KeyValuePair<string, CustomScenarioValue> Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }


    private static void Action(TaskEditorValueToScenarioMethodNode x, AvaloniaPropertyChangedEventArgs e)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (x.Source.Key == null || x.Source.Value == null)
        {
            return;
        }

        switch (x.ValueType)
        {
            case ValueType.None:
                return;
            case ValueType.TempValue:
                KeyValuePair<string, CustomScenarioValue> values = x.Source;
                if (x.ValueForward == ValueForward.Set)
                {
                    x.Node = new ScenarioMethod(ScenarioMethodType.TempVariableSet) { ValueName = values.Key }
                        .GenerateNode();
                }
                else
                {
                    x.Node = new ScenarioMethod(ScenarioMethodType.TempVariableGet) { ValueName = values.Key }
                        .GenerateNode();
                }

                break;
            case ValueType.StoredValue:
                KeyValuePair<string, CustomScenarioValue> values2 = x.Source;
                if (x.ValueForward == ValueForward.Set)
                {
                    x.Node = new ScenarioMethod(ScenarioMethodType.VariableSet)
                        { ValueName = values2.Key, ValueDataType = values2.Value.Type }.GenerateNode();
                }
                else
                {
                    x.Node = new ScenarioMethod(ScenarioMethodType.VariableGet)
                        { ValueName = values2.Key, ValueDataType = values2.Value.Type }.GenerateNode();
                }

                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
}