using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Utils;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace KitopiaEx.Ocr;

public partial class AdaptiveTextBox : SelectableTextBlock
{
    public Point TopLeft
    {
        get => GetValue(TopLeftProperty);
        set => SetValue(TopLeftProperty, value);
    }

    public static readonly StyledProperty<Point> TopLeftProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, Point>(nameof(TopLeft));

    public Point BottomRight
    {
        get => GetValue(BottomRightProperty);
        set => SetValue(BottomRightProperty, value);
    }

    public static readonly StyledProperty<Point> BottomRightProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, Point>(nameof(BottomRight));
    //Text
    public AdaptiveTextBox()
    {
        
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Calculate the width and height from the rectangle's coordinates
        double width = Math.Abs(BottomRight.X - TopLeft.X+5);
        double height = Math.Abs(BottomRight.Y - TopLeft.Y+5);
        SelectionBrush=new SolidColorBrush(Colors.Cyan,0.7d);

        Width = width;
        Height = height;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        TextWrapping = TextWrapping.NoWrap;

        Background = new SolidColorBrush(Colors.Gray, 0.7d);
        Foreground = new SolidColorBrush(Colors.White);
       
        this.FontSize = height /1.5; 
       
      
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        //e.Pointer.Capture(TopLevel.GetTopLevel(this));
        
    }
}