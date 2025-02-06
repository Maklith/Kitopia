using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Ursa.Controls;


namespace KitopiaEx.ImagePin;

public partial class ImagePin : UrsaWindow
{
    public ImagePin()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Image.Source is Bitmap bitmap)
        {
            bitmap.Dispose();
        }
    }

    private void Image_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

  
}