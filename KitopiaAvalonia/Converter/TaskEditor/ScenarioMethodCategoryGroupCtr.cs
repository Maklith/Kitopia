using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Core.CustomScenario;

namespace KitopiaAvalonia.Converter.TaskEditor;

public class ScenarioMethodCategoryGroupCtr : IValueConverter
{
    public IDataTemplate DataTemplate;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is CompiledBindingExtension compiledBindingExtension)
            if (compiledBindingExtension.DefaultAnchor.Target is Control control)
            {
                control.TryGetResource("DataTemplate", null, out var dataTemplate);
                DataTemplate = dataTemplate as IDataTemplate;
            }

        var expander = new Expander();

        var itemsControl = new StackPanel();
        itemsControl.Spacing = 5;

        //itemsControl.ItemTemplate=itemsControl.GetR
        if (value is ScenarioMethodCategoryGroup group)
        {
            expander.Header = "节点";


            expander.Content = itemsControl;
            Prase(group, itemsControl);
        }

        return expander;
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
            if (DataTemplate.Match(value))
            {
                var control = DataTemplate.Build(value);
                control.DataContext = value;
                itemsControl.Children.Add(control);
            }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}