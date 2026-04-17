using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vanara.PInvoke;

namespace KitopiaAvalonia.Services;

public partial class ToastShowWindow : Window
{
    private readonly DispatcherTimer _regionSyncTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };

    public ToastShowWindow()
    {
        InitializeComponent();
        _regionSyncTimer.Tick += (_, _) => UpdateWindowRegionToToastCards();

        Opened += (_, _) =>
        {
            Reposition();
            ScrollToLatest();
            UpdateWindowRegionToToastCards();
        };
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Reposition();
                UpdateWindowRegionToToastCards();
            }
        };
    }

    public void Reposition()
    {
        User32.GetCursorPos(out var pos);
        var screen = Screens.ScreenFromPoint(new PixelPoint(pos.X, pos.Y)) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var margin = 10;
        var workingArea = screen.WorkingArea;
        var width = Math.Max(1, (int)Math.Ceiling((Bounds.Width > 0 ? Bounds.Width : Width) * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling((Bounds.Height > 0 ? Bounds.Height : Height) * RenderScaling));

        var targetX = workingArea.Right - width - margin;
        var targetY = workingArea.Bottom - height - margin;

        var minX = workingArea.X + margin;
        var minY = workingArea.Y + margin;
        var maxX = Math.Max(minX, workingArea.Right - width - margin);
        var maxY = Math.Max(minY, workingArea.Bottom - height - margin);

        targetX = Math.Clamp(targetX, minX, maxX);
        targetY = Math.Clamp(targetY, minY, maxY);

        Position = new PixelPoint(targetX, targetY);
    }

    public void ScrollToLatest()
    {
        Dispatcher.UIThread.Post(() => { ToastScrollViewer.ScrollToEnd(); }, DispatcherPriority.Background);
    }

    protected override void IsVisibleChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.IsVisibleChanged(e);
        if (e.NewValue is true)
        {
            _regionSyncTimer.Start();
            Reposition();
            ScrollToLatest();
            UpdateWindowRegionToToastCards();
        }
        else
        {
            _regionSyncTimer.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _regionSyncTimer.Stop();
        base.OnClosed(e);
    }

    private void UpdateWindowRegionToToastCards()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var hwnd = TryGetPlatformHandle()?.Handle;
        if (!hwnd.HasValue)
        {
            return;
        }

        var scale = RenderScaling <= 0 ? 1 : RenderScaling;
        var shadowPadding = Math.Max(1, (int)Math.Ceiling(12 * scale));
        var unionRegion = Gdi32.CreateRectRgn(0, 0, 0, 0);

        try
        {
            var cards = this.GetVisualDescendants()
                .OfType<Control>()
                .Where(control => control.Classes.Contains("toast-card")
                                  && control.IsVisible
                                  && control.Bounds.Width > 0
                                  && control.Bounds.Height > 0);

            foreach (var card in cards)
            {
                var topLeft = card.TranslatePoint(default, this);
                if (topLeft is null)
                {
                    continue;
                }

                var left = (int)Math.Floor(topLeft.Value.X * scale) - shadowPadding;
                var top = (int)Math.Floor(topLeft.Value.Y * scale) - shadowPadding;
                var right = (int)Math.Ceiling((topLeft.Value.X + card.Bounds.Width) * scale) + shadowPadding;
                var bottom = (int)Math.Ceiling((topLeft.Value.Y + card.Bounds.Height) * scale) + shadowPadding;

                var cardRegion = Gdi32.CreateRectRgn(left, top, right, bottom);
                Gdi32.CombineRgn(unionRegion, unionRegion, cardRegion, Gdi32.RGN_COMB.RGN_OR);
                Gdi32.DeleteObject(cardRegion);
            }

            User32.SetWindowRgn((HWND)hwnd.Value, unionRegion, true);
        }
        catch
        {
            Gdi32.DeleteObject(unionRegion);
            throw;
        }
    }

    private void ToastCard_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not StyledElement element || element.DataContext is not ToastItemViewModel toastItem)
        {
            return;
        }

        if (e.Source is Button)
        {
            return;
        }

        if (toastItem.ClickCommand is not null && toastItem.ClickCommand.CanExecute(null))
        {
            toastItem.ClickCommand.Execute(null);
            e.Handled = true;
        }
    }
}
