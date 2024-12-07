using Avalonia;
using Avalonia.Controls;

namespace KitopiaAvalonia.Controls.Capture;

public class CaptureToolBase : ContentControl
{
    public static readonly StyledProperty<bool> IsSelectedProperty =
        AvaloniaProperty.Register<CaptureToolBase, bool>(nameof(IsSelected));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}