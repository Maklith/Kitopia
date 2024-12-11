using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace KitopiaEx.Ocr;

public partial class OcrResultShowWindow : Window
{
    private ScaleTransform _scaleTransform;
    public OcrResultShowWindow()
    {
        InitializeComponent();
        _scaleTransform = new ScaleTransform();
        
        
        ItemsControl.RenderTransform = _scaleTransform;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        ItemsControl.Width = Image.Source.Size.Width;
        ItemsControl.Height =Image.Source.Size.Height;
        double scale = Image.Bounds.Size.Width / Image.Source.Size.Width;
        _scaleTransform.ScaleX = scale;
        _scaleTransform.ScaleY = scale;
        
        
        ItemsControl.RenderTransform = _scaleTransform;
        ItemsControl.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Absolute);
    }


    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        
        if (Image.Source is not null)
        {
            double scale = (Image.Bounds.Size.Width / e.PreviousSize.Width)*e.NewSize.Width / Image.Source.Size.Width;
            Image.Width=e.NewSize.Width;
            Image.Height=e.NewSize.Height;
            _scaleTransform.ScaleX = scale;
            _scaleTransform.ScaleY = scale;
        }
    }

    protected override void OnResized(WindowResizedEventArgs e)
    {
        base.OnResized(e);
        
        
    }
}