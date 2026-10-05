using System.ComponentModel;

namespace KitopiaEx.ImageCompression;

public enum ImageCompressionMode
{
    [Description("lang.kitopiaex.compression.quality_mode")] Quality,
    [Description("lang.kitopiaex.compression.target_mode")] TargetSize
}

public enum ImageCompressionFormat
{
    WebP,
    JPEG,
    [Description("lang.kitopiaex.compression.png_lossless")] PNG
}

public sealed record ImageCompressionOptions
{
    public ImageCompressionMode Mode { get; init; }
    public ImageCompressionFormat Format { get; init; } = ImageCompressionFormat.WebP;
    public int Quality { get; init; } = 80;
    public int TargetPercent { get; init; } = 50;
    public int ResizePercent { get; init; } = 100;
    public int MaxDimension { get; init; }
    public string OutputDirectory { get; init; } = "";
    public bool SkipLarger { get; init; } = true;
}

public sealed record ImageCompressionResult(string? OutputPath, long OriginalBytes, long OutputBytes,
    int Width, int Height, bool TargetMet);
