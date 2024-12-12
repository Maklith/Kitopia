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
        Image.SizeChanged += OnSizeChanged;
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Image.SizeChanged -= OnSizeChanged;
    }


    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        
        
        if (Image.Source is not null)
        {
            
            _scaleTransform.ScaleX/=  (e.PreviousSize.Width / e.NewSize.Width);
            _scaleTransform.ScaleY /=  (e.PreviousSize.Width / e.NewSize.Width);
        }
    }
}