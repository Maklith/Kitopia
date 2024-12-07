using Avalonia.Controls;
using Avalonia.Interactivity;

namespace KitopiaAvalonia.Windows;

public partial class ErrorDialog : Window
{
    public ErrorDialog(string? infostr, string str)
    {
        InitializeComponent();
        info.Text = infostr;
        text.Text = str;
    }

    private void Button_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void logs_Click(object? sender, RoutedEventArgs e)
    {
        throw new System.NotImplementedException();
    }
}