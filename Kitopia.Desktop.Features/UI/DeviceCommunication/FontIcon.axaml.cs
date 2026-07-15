using Avalonia;
using Avalonia.Controls.Primitives;

namespace Kitopia.Desktop.Features.UI.DeviceCommunication;

public class FontIcon : TemplatedControl
{
    public static readonly StyledProperty<string> GlyphProperty =
        AvaloniaProperty.Register<FontIcon, string>(nameof(Glyph));

    public string Glyph
    {
        get => GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public static readonly StyledProperty<string> GlyphFilledProperty =
        AvaloniaProperty.Register<FontIcon, string>(nameof(GlyphFilled));

    public string GlyphFilled
    {
        get => GetValue(GlyphFilledProperty);
        set => SetValue(GlyphFilledProperty, value);
    }

    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<FontIcon, bool>(nameof(IsFilled));

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }
}
