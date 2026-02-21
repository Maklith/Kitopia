using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Core.Utils;

namespace KitopiaAvalonia.Controls;

public partial class DialogOvercover : UserControl
{
    public static readonly StyledProperty<bool> CanDismissProperty =
        AvaloniaProperty.Register<DialogOvercover, bool>(nameof(CanDismiss));

    public bool CanDismiss
    {
        get => GetValue(CanDismissProperty);
        set => SetValue(CanDismissProperty, value);
    }

    private const string s_pcPrimary = ":primary";
    private const string s_pcSecondary = ":secondary";
    private const string s_pcClose = ":close";
    private bool _mouseDownForWindowMoving;
    private PointerPoint _originalPoint;

    public DialogOvercover()
    {
        InitializeComponent();
    }

    public DialogOvercover(DialogContent content, bool canDismiss = false)
    {
        CanDismiss = canDismiss;
        InitializeComponent();
        Title.Text = content.Title;
        Content.Content = content.Content;


        if (content.PrimaryButtonText != null)
        {
            PrimaryButton.Content = content.PrimaryButtonText;

            PrimaryButton.Click += (sender, args) =>
            {
                IsVisible = false;
                content.PrimaryAction?.Invoke();
            };
            PrimaryButton.IsVisible = true;
        }

        if (content.CloseButtonText != null)
        {
            CloseButton.Content = content.CloseButtonText;
            CloseButton.Click += (sender, args) =>
            {
                IsVisible = false;
                content.CloseAction?.Invoke();
            };
            CloseButton.IsVisible = true;
        }

        if (content.SecondaryButtonText != null)
        {
            SecondaryButton.Content = content.SecondaryButtonText;
            SecondaryButton.Click += (sender, args) =>
            {
                IsVisible = false;
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
        // Position = new PixelPoint(Position.X + (int)(currentPoint.Position.X - _originalPoint.Position.X),
        //     Position.Y + (int)(currentPoint.Position.Y - _originalPoint.Position.Y));
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (CanDismiss && sender is DialogOvercover)
        {
            IsVisible = false;
            e.Handled = true;
            return;
        }

        _mouseDownForWindowMoving = true;
        _originalPoint = e.GetCurrentPoint(this);
        e.Handled = true;
    }

    private void InputElement_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _mouseDownForWindowMoving = false;
    }


    private void Visual_OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var visualParent = this.GetVisualParent();
        BackgroundElement.Width = visualParent.Bounds.Width / 2.5;
        BackgroundElement.Height = visualParent.Bounds.Height / 2;
    }
}