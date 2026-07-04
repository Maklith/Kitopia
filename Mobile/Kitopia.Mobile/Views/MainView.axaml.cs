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

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (TopLevelContext is not null)
        {
            TopLevelContext.CurrentTopLevel = TopLevel.GetTopLevel(this);
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        var topLevelContext = TopLevelContext;
        if (topLevelContext is not null && topLevelContext.CurrentTopLevel == TopLevel.GetTopLevel(this))
        {
            topLevelContext.CurrentTopLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }
}
