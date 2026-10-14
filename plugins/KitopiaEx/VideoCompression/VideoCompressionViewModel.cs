using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitopiaEx.ImageCompression;
using PluginCore.Localization;
using PluginCore.Media;

namespace KitopiaEx.VideoCompression;

public sealed partial class VideoCompressionViewModel : ObservableObject, IDisposable
{
    private static readonly VideoCompressionCodec[] AllCodecs = [VideoCompressionCodec.KeepOriginal, VideoCompressionCodec.H264,
        VideoCompressionCodec.HEVC, VideoCompressionCodec.VP9, VideoCompressionCodec.AV1];
    private static readonly VideoCompressionCodec[] WebMCodecs = [VideoCompressionCodec.KeepOriginal, VideoCompressionCodec.VP9, VideoCompressionCodec.AV1];
    private static readonly VideoCompressionCodec[] MovCodecs = [VideoCompressionCodec.KeepOriginal, VideoCompressionCodec.H264, VideoCompressionCodec.HEVC];
    private readonly VideoCompressor _compressor = new();
    private readonly HashSet<string> _paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private Task _pendingImport = Task.CompletedTask;
    private bool _disposed;

    public VideoCompressionViewModel(Ffmpeg ffmpeg)
    {
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasOutputDirectory));
            OnPropertyChanged(nameof(Summary));
            StartCommand.NotifyCanExecuteChanged();
        };
        try
        {
            _ = ffmpeg.Version;
            ToolsAvailable = File.Exists(Path.Combine(VideoCompressor.ToolDirectory, "ffmpeg.exe")) &&
                File.Exists(Path.Combine(VideoCompressor.ToolDirectory, "ffprobe.exe"));
        }
        catch (Exception) { ToolsAvailable = false; }
        if (!ToolsAvailable) Message = Lang.Get("lang.kitopiaex.video_compression.ffmpeg_missing");
    }

    // The image compressor's file item already owns the shared size, output and status presentation.
    public ObservableCollection<ImageCompressionItem> Items { get; } = [];
    public VideoCompressionFormat[] Formats { get; } = [VideoCompressionFormat.KeepOriginal, VideoCompressionFormat.MP4,
        VideoCompressionFormat.WebM, VideoCompressionFormat.MKV, VideoCompressionFormat.MOV];
    public VideoCompressionCodec[] Codecs => Format switch
    {
        VideoCompressionFormat.WebM => WebMCodecs,
        VideoCompressionFormat.MOV => MovCodecs,
        _ => AllCodecs
    };
    public bool IsEmpty => Items.Count == 0;
    public bool HasOutputDirectory => !IsEmpty || !string.IsNullOrWhiteSpace(OutputDirectory);
    public bool CanStart => !_disposed && !IsBusy && !IsAdding && !IsEmpty && ToolsAvailable;
    public bool CanEdit => !_disposed && !IsBusy && !IsAdding;
    public bool IsQualityMode { get => Mode == VideoCompressionMode.Quality; set { if (value) Mode = VideoCompressionMode.Quality; } }
    public bool IsTargetMode { get => Mode == VideoCompressionMode.TargetSize; set { if (value) Mode = VideoCompressionMode.TargetSize; } }
    public bool IsConversionMode { get => Mode == VideoCompressionMode.Conversion; set { if (value) Mode = VideoCompressionMode.Conversion; } }
    public bool IsCompressionMode => !IsConversionMode;
    public bool CanAdjustHdr => IsCompressionMode || Codec != VideoCompressionCodec.KeepOriginal;
    public string StartLabelKey => IsConversionMode ? "lang.kitopiaex.video_compression.start_conversion" : "lang.kitopiaex.compression.start";
    public double BatchProgress => ProcessedCount + FileProgress;
    public string Summary => Lang.Format(IsConversionMode ? "lang.kitopiaex.video_compression.conversion_summary" : "lang.kitopiaex.video_compression.summary", Items.Count,
        ImageCompressionItem.FormatSize(Items.Sum(item => item.OriginalBytes)),
        ImageCompressionItem.FormatSize(Items.Where(item => item.OutputPath != null)
            .Sum(item => IsConversionMode ? item.OutputBytes ?? 0 : item.OriginalBytes - (item.OutputBytes ?? item.OriginalBytes))));

    public bool ToolsAvailable { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isAdding;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchProgress))]
    private int _processedCount;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatchProgress))]
    private double _fileProgress;
    [ObservableProperty] private ImageCompressionItem? _selectedItem;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQualityMode))]
    [NotifyPropertyChangedFor(nameof(IsTargetMode))]
    [NotifyPropertyChangedFor(nameof(IsConversionMode))]
    [NotifyPropertyChangedFor(nameof(IsCompressionMode))]
    [NotifyPropertyChangedFor(nameof(CanAdjustHdr))]
    [NotifyPropertyChangedFor(nameof(StartLabelKey))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private VideoCompressionMode _mode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Codecs))]
    private VideoCompressionFormat _format;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdjustHdr))]
    private VideoCompressionCodec _codec;
    [ObservableProperty] private int _quality = 80;
    [ObservableProperty] private int _targetPercent = 50;
    [ObservableProperty] private int _resizePercent = 100;
    [ObservableProperty] private int _maxDimension;
    [ObservableProperty] private bool _keepAudio = true;
    [ObservableProperty] private bool _convertHdrToSdr = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutputDirectory))]
    private string _outputDirectory = "";
    [ObservableProperty] private bool _skipLarger = true;

    partial void OnFormatChanged(VideoCompressionFormat value)
    {
        if (value == VideoCompressionFormat.WebM && Codec is VideoCompressionCodec.H264 or VideoCompressionCodec.HEVC)
            Codec = VideoCompressionCodec.VP9;
        else if (value == VideoCompressionFormat.MOV && Codec is VideoCompressionCodec.VP9 or VideoCompressionCodec.AV1)
            Codec = VideoCompressionCodec.H264;
    }

    public Task AddPathsAsync(IEnumerable<string> paths)
    {
        var previousImport = _pendingImport;
        return _pendingImport = ImportAsync();

        async Task ImportAsync()
        {
            await previousImport;
            if (StartCommand.ExecutionTask is { IsCompleted: false } compression) await compression;
            if (_disposed) return;
            IsAdding = true;
            try
            {
                var files = await Task.Run(() => paths.SelectMany(path => Directory.Exists(path)
                    ? Directory.EnumerateFiles(path).Where(VideoCompressor.IsSupported) : new[] { path }).ToArray());
                foreach (var file in files)
                {
                    if (_disposed) break;
                    var path = Path.GetFullPath(file);
                    if (!VideoCompressor.IsSupported(path) || _paths.Contains(path)) continue;
                    try
                    {
                        var bytes = new FileInfo(path).Length;
                        _paths.Add(path);
                        Items.Add(new ImageCompressionItem(path, bytes, null));
                    }
                    catch (Exception exception) { Message = exception.Message; }
                }
            }
            catch (Exception exception) { Message = exception.Message; }
            finally { IsAdding = false; }
        }
    }

    [RelayCommand]
    private void Remove(ImageCompressionItem? item)
    {
        if (!CanEdit || item == null) return;
        Items.Remove(item);
        _paths.Remove(item.SourcePath);
        if (SelectedItem == item) SelectedItem = null;
    }

    [RelayCommand]
    private void Clear()
    {
        if (!CanEdit) return;
        Items.Clear();
        _paths.Clear();
        SelectedItem = null;
        ProcessedCount = 0;
        FileProgress = 0;
        ProgressText = "";
    }

    [RelayCommand(CanExecute = nameof(CanStart), IncludeCancelCommand = true)]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = new VideoCompressionOptions
        {
            Mode = Mode,
            Format = Format,
            Codec = Codec,
            Quality = Quality,
            TargetPercent = TargetPercent,
            ResizePercent = ResizePercent,
            MaxDimension = MaxDimension,
            KeepAudio = KeepAudio,
            ConvertHdrToSdr = ConvertHdrToSdr,
            OutputDirectory = OutputDirectory,
            SkipLarger = SkipLarger
        };
        IsBusy = true;
        Message = "";
        ProcessedCount = 0;
        FileProgress = 0;
        var failed = 0;
        foreach (var item in Items)
        {
            item.Status = ImageCompressionStatus.Waiting;
            item.OutputPath = null;
            item.OutputBytes = null;
            item.Error = "";
        }
        try
        {
            foreach (var item in Items)
            {
                if (cancellationToken.IsCancellationRequested) break;
                item.Status = ImageCompressionStatus.Running;
                FileProgress = 0;
                var progress = new Progress<double>(value =>
                {
                    if (item.Status != ImageCompressionStatus.Running) return;
                    FileProgress = value;
                    ProgressText = Lang.Format("lang.kitopiaex.video_compression.progress", ProcessedCount + 1,
                        Items.Count, item.FileName, value * 100);
                });
                ProgressText = Lang.Format("lang.kitopiaex.video_compression.progress", ProcessedCount + 1, Items.Count, item.FileName, 0);
                try
                {
                    var result = await _compressor.CompressAsync(item.SourcePath, options, progress, cancellationToken);
                    item.OriginalBytes = result.OriginalBytes;
                    item.OutputBytes = result.OutputBytes;
                    item.OutputPath = result.OutputPath;
                    item.Status = result.OutputPath == null ? ImageCompressionStatus.Skipped
                        : result.TargetMet ? ImageCompressionStatus.Completed : ImageCompressionStatus.TargetUnmet;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    item.Status = ImageCompressionStatus.Cancelled;
                    break;
                }
                catch (Exception exception)
                {
                    item.Status = ImageCompressionStatus.Failed;
                    item.Error = exception.Message;
                    failed++;
                }
                FileProgress = 0;
                ProcessedCount++;
                OnPropertyChanged(nameof(Summary));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var item in Items.Where(item => item.Status == ImageCompressionStatus.Waiting))
                    item.Status = ImageCompressionStatus.Cancelled;
                ProgressText = Lang.Get("lang.kitopiaex.compression.cancelled");
            }
            else ProgressText = Lang.Format("lang.kitopiaex.video_compression.finished", ProcessedCount, failed);
        }
        finally { FileProgress = 0; IsBusy = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StartCancelCommand.Execute(null);
        StartCommand.NotifyCanExecuteChanged();
    }
}
