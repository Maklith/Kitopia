using Avalonia;
using Avalonia.Controls;
using Core.ViewModel.Pages;

namespace Core.UiControls.Plugin;

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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
    }
}