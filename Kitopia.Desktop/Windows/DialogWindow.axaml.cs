using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Kitopia.Desktop.Features.Utils;

namespace Kitopia.Desktop.Windows;

public partial class DialogWindow : Window
{
    private const string s_pcPrimary = ":primary";
    private const string s_pcSecondary = ":secondary";
    private const string s_pcClose = ":close";
    private bool _mouseDownForWindowMoving = false;
    private PointerPoint _originalPoint;

    public DialogWindow()
    {
        InitializeComponent();
    }

    public DialogWindow(DialogContent content)
    {
        InitializeComponent();
        Title.Text = content.Title;
        Content.Content = content.Content;


        if (content.PrimaryButtonText != null)
        {
            PrimaryButton.Content = content.PrimaryButtonText;

            PrimaryButton.Click += (sender, args) =>
            {
                Close();
                content.PrimaryAction?.Invoke();
            };
            PrimaryButton.IsVisible = true;
        }

        if (content.CloseButtonText != null)
        {
            CloseButton.Content = content.CloseButtonText;
            CloseButton.Click += (sender, args) =>
            {
                Close();
                content.CloseAction?.Invoke();
            };
            CloseButton.IsVisible = true;
        }

        if (content.SecondaryButtonText != null)
        {
            SecondaryButton.Content = content.SecondaryButtonText;
            SecondaryButton.Click += (sender, args) =>
            {
                Close();
                content.SecondaryAction?.Invoke();
            };
            SecondaryButton.IsVisible = true;
        }

        var hasPrimary = content.PrimaryButtonText != null;
        var hasSecondary = content.SecondaryButtonText != null;
        var hasClose = content.CloseButtonText != null;
        if (hasPrimary && hasSecondary && hasClose)
        {
            PrimaryButton.SetValue(Grid.ColumnProperty, 0);
            PrimaryButton.Margin = new Thickness(0, 0, 4, 0);
            SecondaryButton.SetValue(Grid.ColumnProperty, 1);
            SecondaryButton.Margin = new Thickness(4, 0, 4, 0);
            CloseButton.SetValue(Grid.ColumnProperty, 2);
            CloseButton.Margin = new Thickness(4, 0, 0, 0);
        }

        if (!hasPrimary && hasSecondary && hasClose)
        {
            PrimaryButton.SetValue(Grid.ColumnProperty, 0);
            PrimaryButton.Margin = new Thickness(0, 0, 4, 0);
            PrimaryButton.SetValue(Grid.ColumnSpanProperty, 2);
            CloseButton.SetValue(Grid.ColumnProperty, 2);
            CloseButton.SetValue(Grid.ColumnSpanProperty, 2);
            CloseButton.Margin = new Thickness(4, 0, 0, 0);
        }

        if (hasPrimary && hasSecondary && !hasClose)
        {
            PrimaryButton.SetValue(Grid.ColumnProperty, 0);
            PrimaryButton.Margin = new Thickness(0, 0, 4, 0);
            PrimaryButton.SetValue(Grid.ColumnSpanProperty, 2);
            SecondaryButton.SetValue(Grid.ColumnProperty, 2);
            SecondaryButton.SetValue(Grid.ColumnSpanProperty, 2);
            SecondaryButton.Margin = new Thickness(4, 0, 0, 0);
        }

        if (hasPrimary && !hasSecondary && hasClose)
        {
            PrimaryButton.SetValue(Grid.ColumnProperty, 0);
            PrimaryButton.Margin = new Thickness(0, 0, 4, 0);
            PrimaryButton.SetValue(Grid.ColumnSpanProperty, 2);
            CloseButton.SetValue(Grid.ColumnProperty, 2);
            CloseButton.SetValue(Grid.ColumnSpanProperty, 2);
            CloseButton.Margin = new Thickness(4, 0, 0, 0);
        }

        if (!hasPrimary && !hasSecondary && hasClose)
        {
            CloseButton.SetValue(Grid.ColumnProperty, 2);
            CloseButton.SetValue(Grid.ColumnSpanProperty, 2);
        }

        if (!hasPrimary && hasSecondary && !hasClose)
        {
            SecondaryButton.SetValue(Grid.ColumnProperty, 2);
            SecondaryButton.SetValue(Grid.ColumnSpanProperty, 2);
        }

        if (hasPrimary && !hasSecondary && !hasClose)
        {
            PrimaryButton.SetValue(Grid.ColumnProperty, 2);
            PrimaryButton.SetValue(Grid.ColumnSpanProperty, 2);
        }
    }

    private void InputElement_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_mouseDownForWindowMoving) return;

        var currentPoint = e.GetCurrentPoint(this);
        Position = new PixelPoint(Position.X + (int)(currentPoint.Position.X - _originalPoint.Position.X),
            Position.Y + (int)(currentPoint.Position.Y - _originalPoint.Position.Y));
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (WindowState == WindowState.Maximized || WindowState == WindowState.FullScreen) return;

        _mouseDownForWindowMoving = true;
        _originalPoint = e.GetCurrentPoint(this);
    }

    private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseDownForWindowMoving = false;
    }
}