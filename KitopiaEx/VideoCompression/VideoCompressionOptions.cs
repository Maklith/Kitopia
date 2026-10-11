using System.ComponentModel;

namespace KitopiaEx.VideoCompression;

public enum VideoCompressionMode
{
    [Description("lang.kitopiaex.compression.quality_mode")] Quality,
    [Description("lang.kitopiaex.compression.target_mode")] TargetSize,
    [Description("lang.kitopiaex.video_compression.convert_mode")] Conversion
}

public enum VideoCompressionFormat
{
    [Description("lang.kitopiaex.video_compression.mp4")] MP4,
    [Description("lang.kitopiaex.video_compression.webm")] WebM,
    [Description("lang.kitopiaex.video_compression.mkv")] MKV,
    [Description("lang.kitopiaex.video_compression.mov")] MOV,
    [Description("lang.kitopiaex.video_compression.keep_original")] KeepOriginal
}

public enum VideoCompressionCodec
{
    [Description("lang.kitopiaex.video_compression.h264")] H264,
    [Description("lang.kitopiaex.video_compression.vp9")] VP9,
    [Description("lang.kitopiaex.video_compression.av1")] AV1,
    [Description("lang.kitopiaex.video_compression.hevc")] HEVC,
    [Description("lang.kitopiaex.video_compression.keep_original")] KeepOriginal
}

public sealed record VideoCompressionOptions
{
    public VideoCompressionMode Mode { get; init; }
    public VideoCompressionFormat Format { get; init; }
    public VideoCompressionCodec Codec { get; init; }
    public int Quality { get; init; } = 80;
    public int TargetPercent { get; init; } = 50;
    public int ResizePercent { get; init; } = 100;
    public int MaxDimension { get; init; }
    public bool KeepAudio { get; init; } = true;
    public bool ConvertHdrToSdr { get; init; } = true;
    public string OutputDirectory { get; init; } = "";
    public bool SkipLarger { get; init; } = true;
}

public sealed record VideoCompressionResult(string? OutputPath, long OriginalBytes, long OutputBytes, bool TargetMet);
