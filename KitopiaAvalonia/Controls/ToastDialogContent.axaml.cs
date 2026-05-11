using Avalonia.Controls;
using Avalonia.Interactivity;
using Irihi.Avalonia.Shared.Contracts;

namespace KitopiaAvalonia.Controls;

public partial class ToastDialogContent : UserControl
{
    public ToastDialogContent()
    {
        InitializeComponent();
        
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IDialogContext dialogContext)
        {
            dialogContext.Close();
        }
    }

   
    
}
