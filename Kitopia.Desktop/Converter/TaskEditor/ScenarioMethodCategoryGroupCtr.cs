using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Windows.TaskEditors;
using Kitopia.Feature.Avalonia.Localization;
using Avalonia.Data;

namespace Kitopia.Desktop.Converter.TaskEditor;

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

        expander.Bind(Expander.HeaderProperty, (BindingBase)new LangExtension("lang.kitopia.node_type").ProvideValue(null!));
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

            expander.Bind(Expander.HeaderProperty, (BindingBase)new LangExtension(
                scenarioMethodCategoryGroup.DisplayName ?? scenarioMethodCategoryGroup.Name).ProvideValue(null!));
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
