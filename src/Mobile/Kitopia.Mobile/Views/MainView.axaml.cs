using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile.Views;

public partial class MainView : UserControl
{
    private MobileTextPromptRequest? _pendingTextPrompt;

    public MainView()
    {
        InitializeComponent();
    }

    public MobileTopLevelContext? TopLevelContext { get; set; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevelContext is not null)
        {
            TopLevelContext.CurrentTopLevel = TopLevel.GetTopLevel(this);
            TopLevelContext.TextPromptHandler = ShowTextPromptAsync;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevelContext is not null)
        {
            if (TopLevelContext.CurrentTopLevel == TopLevel.GetTopLevel(this))
            {
                TopLevelContext.CurrentTopLevel = null;
            }

            if (TopLevelContext.TextPromptHandler == ShowTextPromptAsync)
            {
                TopLevelContext.TextPromptHandler = null;
            }
        }

        CompleteTextPrompt(value: null);

        base.OnDetachedFromVisualTree(e);
    }

    private async Task ShowTextPromptAsync(MobileTextPromptRequest request)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _pendingTextPrompt?.TryCancel();
            _pendingTextPrompt = request;

            this.FindControl<TextBlock>("TextPromptTitle")!.Text = request.Title;
            this.FindControl<TextBlock>("TextPromptMessage")!.Text = request.Prompt;
            var input = this.FindControl<TextBox>("TextPromptInput")!;
            input.Text = request.InitialValue ?? string.Empty;
            this.FindControl<Border>("TextPromptOverlay")!.IsVisible = true;
            input.Focus();
            input.SelectAll();
        });
    }

    private void OnTextPromptConfirmClicked(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CompleteTextPrompt(this.FindControl<TextBox>("TextPromptInput")?.Text);
    }

    private void OnTextPromptCancelClicked(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        CompleteTextPrompt(value: null);
    }

    private void CompleteTextPrompt(string? value)
    {
        var request = _pendingTextPrompt;
        _pendingTextPrompt = null;

        var overlay = this.FindControl<Border>("TextPromptOverlay");
        if (overlay is not null)
        {
            overlay.IsVisible = false;
        }

        if (value is null)
        {
            request?.TryCancel();
        }
        else
        {
            request?.TryComplete(value);
        }
    }
}
