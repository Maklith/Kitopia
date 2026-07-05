using Avalonia;
using Avalonia.Controls;
using Kitopia.Mobile.Services;

namespace Kitopia.Mobile.Views;

public partial class MainView : UserControl
{
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
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevelContext is not null && TopLevelContext.CurrentTopLevel == TopLevel.GetTopLevel(this))
        {
            TopLevelContext.CurrentTopLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }
}
