using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Kitopia.Desktop.Controls;

public class HotKeyShowItme : TemplatedControl
{
    public static readonly StyledProperty<Control> IconProperty =
        AvaloniaProperty.Register<HotKeyShowItme, Control>(nameof(Icon));

    public static readonly StyledProperty<object> ContentProperty =
        AvaloniaProperty.Register<HotKeyShowItme, object>(nameof(Content));

    /// <summary>
    ///     Gets or sets displayed <see cref="IconElement" />.
    /// </summary>
    [Bindable(true)]
    [Category("Appearance")]
    public Control? Icon
    {
        get => (Control)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    [Bindable(true)]
    [Category("ContentProperty")]
    public object? Content
    {
        get => (object)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }
}