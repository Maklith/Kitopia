using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Core.CustomScenario;
using KitopiaAvalonia.Windows.TaskEditors;

namespace KitopiaAvalonia.Converter.TaskEditor;

public class ScenarioMethodCategoryGroupCtr : IValueConverter
{
    private IDataTemplate? _dataTemplate;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is IDataTemplate template)
        {
            _dataTemplate = template;
        }
        else if (parameter is Control control)
        {
            control.TryGetResource("ScenarioMethodNode", null, out var dataTemplate);
            _dataTemplate = dataTemplate as IDataTemplate;
        }

        _dataTemplate ??= new NodeTemplatesSelector { TemplateType = NodeRenderType.View };

        if (value is not ScenarioMethodCategoryGroup group)
        {
            return null;
        }

        var expander = new Expander();

        var itemsControl = new StackPanel();
        itemsControl.Spacing = 5;

        expander.Header = "节点";
        expander.Content = itemsControl;
        Prase(group, itemsControl);

        return expander;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }

    private void Prase(ScenarioMethodCategoryGroup group, StackPanel itemsControl)
    {
        foreach (var (key, scenarioMethodCategoryGroup) in group.Childrens)
        {
            var expander = new Expander();
            itemsControl.Children.Add(expander);

            expander.Header = scenarioMethodCategoryGroup.Name;
            var control = new StackPanel();
            control.Spacing = 5;
            expander.Content = control;
            Prase(scenarioMethodCategoryGroup, control);
        }

        foreach (var (key, value) in group.Methods)
            if (_dataTemplate!.Match(value))
            {
                var control = _dataTemplate.Build(value);
                control.DataContext = value;
                itemsControl.Children.Add(control);
            }
    }
}
