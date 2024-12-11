using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace KitopiaEx.Ocr;

public partial class AdaptiveTextBox : UserControl
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
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<AdaptiveTextBox, string>(nameof(Text));
    public AdaptiveTextBox()
    {
        
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Calculate the width and height from the rectangle's coordinates
        double width = Math.Abs(BottomRight.X - TopLeft.X+5);
        double height = Math.Abs(BottomRight.Y - TopLeft.Y+5);

        // Create the TextBox
        var textBox = new TextBox
        {
            Width = width,
            Height = height,
            HorizontalAlignment =  HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.NoWrap,
            IsReadOnly = true,
            Background = new SolidColorBrush(Colors.Gray,0.7d),
            Foreground = new SolidColorBrush(Colors.White),
            AcceptsReturn = true // Allow multiline input if required
        };
        var binding = new Binding("Text");
        textBox.Bind(TextBox.TextProperty, binding);

        // Set the TextBox's position based on the rectangle's top-left corner

        // Optional: Set styles for the TextBox (e.g., font size, alignment)
        textBox.FontSize = height / 2.1; // Example: Adjust font size based on height

        // Add the TextBox to a Canvas for positioning
       
        Content = textBox;
    }
}