namespace Kitopia.Desktop.Abstractions.TextSelection;

public readonly record struct TextSelectionBounds(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}
