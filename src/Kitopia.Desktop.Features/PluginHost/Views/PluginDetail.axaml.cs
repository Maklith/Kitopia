using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Irihi.Avalonia.Shared.Contracts;
using Ursa.Controls;

namespace Kitopia.Desktop.Features.UI.UiControls.Plugin;

public partial class PluginDetail : UserControl
{
    public static AvaloniaProperty<Control> ContentProperty =
        AvaloniaProperty.Register<PluginDetail, Control>(nameof(Content));

    public Control Content
    {
        get => (Control)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }


    public PluginDetail()
    {
        InitializeComponent();
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is IDialogContext dialogContext)
        {
            dialogContext.Close();
        }
        else
        {
            this.FindAncestorOfType<DialogControlBase>()?.Close();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
    }
}
