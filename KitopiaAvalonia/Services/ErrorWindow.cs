using Avalonia.Threading;
using Core.Services;
using KitopiaAvalonia.Windows;

namespace KitopiaAvalonia.Services;

public class ErrorWindow : IErrorWindow
{
    public void ShowErrorWindow(string title, string message)
    {
        Dispatcher.UIThread.InvokeAsync(() => { new ErrorDialog(title, message).Show(); });
    }
}