using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenCvSharp;
using PluginCore.ExMethod;
using Window = Avalonia.Controls.Window;

namespace Kitopia.Desktop.Windows;

public partial class LongScreenshotProgressWindow : Window
{
    public bool IsStopRequested { get; private set; } = false;

    public LongScreenshotProgressWindow()
    {
        InitializeComponent();
    }

    public void UpdateImage(Mat mat)
    {
        // Convert to bitmap synchronously to ensure the Mat is valid during conversion.
        // If we deferred this inside InvokeAsync, 'mat' might be disposed by the main loop before the UI thread runs.
        var bitmap = mat.ToAWriteableBitmap();
        
        // Must run on UI thread to update the control
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (PreviewImage.Source is Bitmap old)
            {
                old.Dispose();
            }
            PreviewImage.Source = bitmap;
        });
    }

    public void RequestStop()
    {
        IsStopRequested = true;
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        RequestStop();
    }
}