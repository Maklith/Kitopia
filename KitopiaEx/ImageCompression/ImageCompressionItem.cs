using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KitopiaEx.ImageCompression;

public enum ImageCompressionStatus
{
    [Description("lang.kitopiaex.compression.waiting")] Waiting,
    [Description("lang.kitopiaex.compression.running")] Running,
    [Description("lang.kitopiaex.compression.completed")] Completed,
    [Description("lang.kitopiaex.compression.target_unmet")] TargetUnmet,
    [Description("lang.kitopiaex.compression.skipped")] Skipped,
    [Description("lang.kitopiaex.compression.failed")] Failed,
    [Description("lang.kitopiaex.compression.cancelled")] Cancelled
}

public sealed partial class ImageCompressionItem(string sourcePath, long originalBytes, Bitmap? thumbnail) : ObservableObject, IDisposable
{
    public string SourcePath { get; } = sourcePath;
    public string FileName => Path.GetFileName(SourcePath);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginalSize))]
    [NotifyPropertyChangedFor(nameof(Reduction))]
    private long _originalBytes = originalBytes;
    public string OriginalSize => FormatSize(OriginalBytes);
    public Bitmap? Thumbnail { get; } = thumbnail;
    public bool IsTemporary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompleted))]
    [NotifyPropertyChangedFor(nameof(IsWarning))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    private ImageCompressionStatus _status;
    [ObservableProperty] private string? _outputPath;
    [ObservableProperty] private string _error = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputSize))]
    [NotifyPropertyChangedFor(nameof(Reduction))]
    private long? _outputBytes;

    public string OutputSize => OutputBytes is { } bytes ? FormatSize(bytes) : "-";
    public bool IsCompleted => Status == ImageCompressionStatus.Completed;
    public bool IsWarning => Status is ImageCompressionStatus.TargetUnmet or ImageCompressionStatus.Skipped;
    public bool IsFailed => Status == ImageCompressionStatus.Failed;
    public bool IsRunning => Status == ImageCompressionStatus.Running;
    public string Reduction => OutputBytes is { } bytes && OriginalBytes > 0
        ? $"{(1 - (double)bytes / OriginalBytes) * 100:0.0}%" : "-";

    public static string FormatSize(long bytes) => Math.Abs(bytes) switch
    {
        < 1024 => $"{bytes} B",
        < 1048576 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / 1048576.0:0.0} MB"
    };

    public void Dispose()
    {
        Thumbnail?.Dispose();
        if (IsTemporary) File.Delete(SourcePath);
    }
}
