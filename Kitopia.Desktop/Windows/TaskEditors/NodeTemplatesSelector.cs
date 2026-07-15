// Author: liaom
// SolutionName: Kitopia
// ProjectName: Kitopia.Desktop
// FileName:NodeTemplatesSelector.cs
// Date: 2025/09/15 18:09
// FileEffect:

using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml.Styling;
using Kitopia.Desktop.Features.CustomScenario;

namespace Kitopia.Desktop.Windows.TaskEditors;

public enum NodeRenderType
{
    Editor,
    View,
    ViewMinimal
}

public class NodeTemplatesSelector : IDataTemplate
{
    public NodeRenderType TemplateType { get; set; } = NodeRenderType.Editor;

    static ResourceInclude Templates { get; } =
        new ResourceInclude(new Uri("avares://Kitopia.Desktop/Windows/TaskEditors/NodeTemplates.axaml"))
        {
            Source = new Uri("avares://Kitopia.Desktop/Windows/TaskEditors/NodeTemplates.axaml")
        };

    public Control? Build(object? param)
    {
        if (Templates.TryGetResource("NodifyEditorNodeTemplates", null, out var template))
        {
            var dataTemplates = (DataTemplates)template!;
            foreach (var dataTemplate in dataTemplates)
            {
                if (dataTemplate.Match(param))
                {
                    var control = dataTemplate.Build(param);
                    control?.Classes.Add(TemplateType.ToString());
                    return control;
                }
            }
        }

        return null;
    }

    public bool Match(object? data)
    {
        return data is ScenarioNodeBase;
    }
}