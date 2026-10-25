using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace Kitopia.Feature.Avalonia.Controls;

public sealed class HoverFlyoutButton : Button
{
    private Control? _flyoutContent;
    private IDisposable? _pendingClose;

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FlyoutProperty)
        {
            if (change.GetOldValue<FlyoutBase?>() is { } previous)
            {
                previous.Hide();
                previous.Opened -= OnFlyoutOpened;
                previous.Closed -= OnFlyoutClosed;
            }

            if (Flyout is { } flyout)
            {
                // The light-dismiss overlay must not make its own hover target lose pointer input.
                if (flyout is PopupFlyoutBase popupFlyout) popupFlyout.OverlayInputPassThroughElement = this;
                flyout.Opened += OnFlyoutOpened;
                flyout.Closed += OnFlyoutClosed;
            }
        }
        else if (change.Property == IsVisibleProperty && !IsVisible)
        {
            Flyout?.Hide();
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pendingClose?.Dispose();
        _pendingClose = null;
        if (Flyout is { IsOpen: false } flyout) flyout.ShowAt(this);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        OnFlyoutContentPointerExited(this, e);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Flyout?.Hide();
        OnFlyoutClosed(this, EventArgs.Empty);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (Flyout is Flyout { Content: Control content })
        {
            _flyoutContent = content;
            content.PointerEntered += OnFlyoutContentPointerEntered;
            content.PointerExited += OnFlyoutContentPointerExited;
        }
    }

    private void OnFlyoutClosed(object? sender, EventArgs e)
    {
        _pendingClose?.Dispose();
        _pendingClose = null;
        if (_flyoutContent is { } content)
        {
            content.PointerEntered -= OnFlyoutContentPointerEntered;
            content.PointerExited -= OnFlyoutContentPointerExited;
            _flyoutContent = null;
        }
    }

    private void OnFlyoutContentPointerEntered(object? sender, PointerEventArgs e)
    {
        _pendingClose?.Dispose();
        _pendingClose = null;
    }

    private void OnFlyoutContentPointerExited(object? sender, PointerEventArgs e)
    {
        _pendingClose?.Dispose();
        // Allow the pointer to cross the gap between the indicator and its popup.
        _pendingClose = DispatcherTimer.RunOnce(() =>
        {
            _pendingClose = null;
            if (!IsPointerOver && _flyoutContent?.IsPointerOver != true) Flyout?.Hide();
        }, TimeSpan.FromMilliseconds(200));
    }
}
