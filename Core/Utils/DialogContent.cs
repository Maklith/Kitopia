namespace Core.Utils;

public record DialogContent
{
    public DialogContent()
    {
    }

    public DialogContent(string title, object content, string? closeButtonText, string? secondaryButtonText,
        string primaryButtonText, Action? primaryAction, Action? secondaryAction, Action? closeAction)
    {
        Title = title;
        Content = content;
        CloseButtonText = closeButtonText;
        SecondaryButtonText = secondaryButtonText;
        PrimaryButtonText = primaryButtonText;
        PrimaryAction = primaryAction;
        SecondaryAction = secondaryAction;
        CloseAction = closeAction;
    }

    public string Title { get; set; }

    public object Content { get; set; }

    public string? CloseButtonText { get; set; }

    public string? SecondaryButtonText { get; set; }

    public string? PrimaryButtonText { get; set; }

    public Action? PrimaryAction { get; set; }

    public Action? SecondaryAction { get; set; }

    public Action? CloseAction { get; set; }
}