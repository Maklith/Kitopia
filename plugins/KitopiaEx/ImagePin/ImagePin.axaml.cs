using System;
using Avalonia.Input;
using Avalonia.Interactivity;
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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
    }

    private void Image_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }


    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
    }
}