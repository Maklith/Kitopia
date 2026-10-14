using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Kitopia.Feature.Avalonia.DeviceCommunication.ViewModels;

namespace Kitopia.Feature.Avalonia.DeviceCommunication.Views;

public class ChatMessageTemplateSelector : IDataTemplate
{
    public IDataTemplate? DefaultMessageTemplate { get; set; }
    public IDataTemplate? FileMessageTemplate { get; set; }

    public Control? Build(object? item)
    {
        var template = item is FileChatMessageItem ? FileMessageTemplate : DefaultMessageTemplate;
        return template?.Build(item);
    }

    public bool Match(object? data) => true;
}
