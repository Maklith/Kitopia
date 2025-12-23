using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenCvSharp;
using Core.Utils.ImageTools;
using PluginCore.ExMethod;
using Window = Avalonia.Controls.Window;

namespace KitopiaAvalonia.Windows;

public partial class LongScreenshotProgressWindow : Window
{
    public bool IsStopRequested { get; private set; } = false;

    public LongScreenshotProgressWindow()
    {
        InitializeComponent();
    }

    public void UpdateImage(Mat mat)
    {
        // Must run on UI thread
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (PreviewImage.Source is Bitmap old)
            {
                old.Dispose();
            }
            PreviewImage.Source = mat.ToAWriteableBitmap();
        });
    }

    private void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        IsStopRequested = true;
    }
}