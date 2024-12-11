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
       
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        double scale = Image.Bounds.Size.Width / Image.Source.Size.Width;
        _scaleTransform = new ScaleTransform(scale, scale);
        ItemsControl.RenderTransform = _scaleTransform;
        ItemsControl.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
    }
}