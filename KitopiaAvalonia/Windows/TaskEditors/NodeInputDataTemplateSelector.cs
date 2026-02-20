#region

using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Metadata;
using Core.CustomScenario;
using Core.Utils;

#endregion

namespace KitopiaAvalonia.Pages;

public class NodeInputDataTemplateSelector : IDataTemplate
{
    // Override the SelectTemplate method
    [Content] public string TemplateType { get; set; } = String.Empty;

    ResourceInclude Templates { get; } =
        new ResourceInclude(new Uri("avares://KitopiaAvalonia/Windows/TaskEditors/NodeInputTemplates.axaml"))
        {
            Source = new Uri("avares://KitopiaAvalonia/Windows/TaskEditors/NodeInputTemplates.axaml")
        };

    public Control? Build(object? item)
    {
        if (item is ConnectorItem pointItem)
        {
            // Check the type of the item and return the corresponding data template from the resources
            if (!pointItem.InputObject.IsSelf || pointItem.InputObject.ShowType == null ||
                pointItem.InputObject.ShowType.BaseType == null)
                return GetTemplate("InputTemplate")
                    .Build(item);

            if (pointItem.isPluginInputConnector)
            {
                var control = pointItem.PluginInputConnector.IDataTemplate.Build(item);
                control.DataContext = pointItem.PluginInputConnector;
                pointItem.PluginInputConnector.Value.Subscribe(x => { pointItem.InputObject.Value = x.Value; });
                pointItem.InputObject.Value = pointItem.PluginInputConnector.Value.Value.Value;
                control!.Styles.Add(pointItem.PluginInputConnector.Style);
                return control;
            }

            if (pointItem.InputObject.ShowType.BaseType.FullName == "System.Enum")
            {
                var control = GetTemplate("EnumTemplate").Build(item);
                var childOfType = control.GetChildOfType<ComboBox>("ComboBox");
                childOfType.ItemsSource = pointItem.InputObject.ShowType.GetEnumValues();
                return control;
            }

            switch (pointItem.InputObject.ShowType.FullName!)
            {
                case "System.String":
                    return GetTemplate("StringTemplate").Build(item);
                case "System.Int32":
                    return GetTemplate("IntTemplate").Build(item);
                case "System.Double":
                    return GetTemplate("DoubleTemplate").Build(item);
                case "System.Boolean":
                    return GetTemplate("BoolTemplate").Build(item);
                case "PluginCore.SearchViewItem":
                    return GetTemplate("SearchItemTemplate").Build(item);
                default:
                    return GetTemplate("InputTemplate").Build(item);
            }
        }

        return null;
    }

    public bool Match(object? data)
    {
        return true;
    }

    private IDataTemplate GetTemplate(string templateName)
    {
        if (templateName == "InputTemplate")
        {
            templateName = TemplateType == "show" ? "ShowInputTemplate" : templateName;
        }

        if (Templates.TryGetResource(templateName, null, out var template))
        {
            return (IDataTemplate)template;
        }

        throw new InvalidOperationException($"Template '{templateName}' not found in resources.");
    }
}