// Author: liaom
// SolutionName: Kitopia
// ProjectName: Kitopia.Desktop
// FileName:TaskEditorValueNodeShow.axaml.cs
// Date: 2025/09/16 20:09
// FileEffect:

using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.Utils;
using PluginCore.CustomScenario;

namespace Kitopia.Desktop.Windows.TaskEditors;

public enum ValueType
{
    None,
    InputValue,
    TempValue,
    StoredValue
}

public enum ValueForward
{
    Set,
    Get,
}

public partial class TaskEditorValueNodeShow : UserControl
{
    public static readonly StyledProperty<ObservableDictionary<string, CustomScenarioValue>> ValuesProperty =
        AvaloniaProperty.Register<TaskEditorValueNodeShow, ObservableDictionary<string, CustomScenarioValue>>(
            nameof(Values));

    public static readonly StyledProperty<ICommand> DelCommandProperty =
        AvaloniaProperty.Register<TaskEditorValueNodeShow, ICommand>(
            nameof(DelCommand));

    public static readonly StyledProperty<ICommand> AddCommandProperty =
        AvaloniaProperty.Register<TaskEditorValueNodeShow, ICommand>(
            nameof(AddCommand));

    public static readonly StyledProperty<string> ValueNameToAddProperty =
        AvaloniaProperty.Register<TaskEditorValueNodeShow, string>(
            nameof(ValueNameToAdd));

    public static readonly StyledProperty<CustomScenarioValueTuple?> ValueTypeToAddProperty =
        AvaloniaProperty.Register<TaskEditorValueNodeShow, CustomScenarioValueTuple?>(
            nameof(ValueTypeToAdd));

    public static readonly StyledProperty<ValueType> ValueTypeProperty =
        AvaloniaProperty.Register<TaskEditorValueNodeShow, ValueType>(
            nameof(ValueType));

    static TaskEditorValueNodeShow()
    {
        ValuesProperty.Changed.AddClassHandler<TaskEditorValueNodeShow>((x, e) =>
        {
            if (x.Values is not { } values1) return;
            x.AddCommand = new RelayCommand(() =>
            {
                if (values1.ContainsKey(x.ValueNameToAdd)) return;
                if (x.ValueType != ValueType.TempValue && x.ValueTypeToAdd == null)
                {
                    return;
                }

                values1.Add(x.ValueNameToAdd,
                    new CustomScenarioValue(x.ValueTypeToAdd == null ? typeof(object) : x.ValueTypeToAdd.Type, null!));

                x.ValueNameToAdd = null!;
            }, () =>
            {
                if (x.ValueType != ValueType.TempValue && x.ValueTypeToAdd == null)
                {
                    return false;
                }

                return !string.IsNullOrWhiteSpace(x.ValueNameToAdd) && !values1.ContainsKey(x.ValueNameToAdd);
            });

            x.DelCommand = new RelayCommand<object>((s) =>
            {
                if (s is not KeyValuePair<string, CustomScenarioValue> str) return;
                values1.Remove(str.Key);
            });
        });
        ValueTypeToAddProperty.Changed.AddClassHandler<TaskEditorValueNodeShow>((x, e) =>
        {
            if (x.AddCommand is RelayCommand cmd)
                cmd.NotifyCanExecuteChanged();
        });
        ValueNameToAddProperty.Changed.AddClassHandler<TaskEditorValueNodeShow>((x, e) =>
        {
            if (x.AddCommand is RelayCommand cmd)
                cmd.NotifyCanExecuteChanged();
        });
    }

    public TaskEditorValueNodeShow()
    {
        InitializeComponent();
    }

    public ValueType ValueType
    {
        get => GetValue(ValueTypeProperty);
        set => SetValue(ValueTypeProperty, value);
    }

    public CustomScenarioValueTuple? ValueTypeToAdd
    {
        get => GetValue(ValueTypeToAddProperty);
        set => SetValue(ValueTypeToAddProperty, value);
    }

    public string ValueNameToAdd
    {
        get => GetValue(ValueNameToAddProperty);
        set => SetValue(ValueNameToAddProperty, value);
    }

    public ICommand AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public ICommand DelCommand
    {
        get => GetValue(DelCommandProperty);
        set => SetValue(DelCommandProperty, value);
    }

    public ObservableDictionary<string, CustomScenarioValue> Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }
}
