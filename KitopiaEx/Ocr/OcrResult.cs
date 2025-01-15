using Avalonia;

namespace KitopiaEx.Ocr;

public struct OcrResult
{
    public Point SPoint { get; set; }
    public Point EPoint { get; set; }
    public string Text { get; set; }
}