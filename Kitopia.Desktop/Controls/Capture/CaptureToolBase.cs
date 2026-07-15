using Avalonia;
using Avalonia.Controls;

namespace Kitopia.Desktop.Controls.Capture;

public class CaptureToolBase : ContentControl
{
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<CaptureToolBase, bool>(nameof(IsSelected));

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}