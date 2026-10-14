using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Kitopia.Desktop.Services;

namespace Kitopia.Desktop.Controls;

public sealed partial class ToastCard : UserControl
{
    public ToastCard()
    {
        InitializeComponent();
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ToastItemViewModel item ||
            !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Button ||
            e.Source is Avalonia.Visual visual && visual.FindAncestorOfType<Button>() is not null) return;

        if (item.ClickCommand?.CanExecute(null) == true)
        {
            item.ClickCommand.Execute(null);
            e.Handled = true;
        }
    }
}
