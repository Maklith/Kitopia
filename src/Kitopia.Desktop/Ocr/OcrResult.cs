using Avalonia;

namespace Kitopia.Desktop.Ocr;

public sealed record OcrResult(string Text, Point SPoint, Point EPoint);
