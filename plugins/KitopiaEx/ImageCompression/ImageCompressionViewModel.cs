using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PluginCore.Localization;
using PluginCore.Media;

namespace KitopiaEx.ImageCompression;

public sealed partial class ImageCompressionViewModel : ObservableObject, IDisposable
{
    private readonly ImageCompressor _compressor;
    private readonly HashSet<string> _paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private Task _pendingImport = Task.CompletedTask;
    private bool _disposed;

    public ImageCompressionViewModel(Ffmpeg ffmpeg)
    {
        Ffmpeg = ffmpeg;
        _compressor = new ImageCompressor(ffmpeg);
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(CanChooseCompressionType));
            OnPropertyChanged(nameof(IsLossy));
            if (!IsLossy) Mode = ImageCompressionMode.Quality;
            OnPropertyChanged(nameof(HasOutputDirectory));
            OnPropertyChanged(nameof(Summary));
            StartCommand.NotifyCanExecuteChanged();
        };
        RefreshFfmpeg();
    }

    public Ffmpeg Ffmpeg { get; }
    public ObservableCollection<ImageCompressionItem> Items { get; } = [];
    public ImageCompressionFormat[] Formats { get; } =
        [ImageCompressionFormat.Original, ImageCompressionFormat.WebP, ImageCompressionFormat.JPEG, ImageCompressionFormat.PNG];
    public bool IsEmpty => Items.Count == 0;
    public bool HasOutputDirectory => !IsEmpty || !string.IsNullOrWhiteSpace(OutputDirectory);
    public bool CanStart => !_disposed && !IsBusy && !IsAdding && !IsEmpty && FfmpegAvailable;
    public bool CanEdit => !_disposed && !IsBusy && !IsAdding;
    public bool IsQualityMode { get => Mode == ImageCompressionMode.Quality; set { if (value) Mode = ImageCompressionMode.Quality; } }
    public bool IsTargetMode { get => Mode == ImageCompressionMode.TargetSize; set { if (value) Mode = ImageCompressionMode.TargetSize; } }
    public bool IsLossyCompression { get => !Lossless; set { if (value) Lossless = false; } }
    public bool CanChooseCompressionType => Format is ImageCompressionFormat.PNG or ImageCompressionFormat.WebP ||
        Format == ImageCompressionFormat.Original && (IsEmpty || Items.Any(item =>
            Path.GetExtension(item.SourcePath).ToLowerInvariant() is ".png" or ".webp"));
    public bool IsLossy => Format == ImageCompressionFormat.JPEG ||
        Format is ImageCompressionFormat.PNG or ImageCompressionFormat.WebP && !Lossless ||
        Format == ImageCompressionFormat.Original && Items.Any(item =>
            Path.GetExtension(item.SourcePath).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" or ".avif" => true,
                ".png" or ".webp" => !Lossless,
                _ => false
            });
    public string Summary => Lang.Format("lang.kitopiaex.compression.summary", Items.Count,
        ImageCompressionItem.FormatSize(Items.Sum(item => item.OriginalBytes)),
        ImageCompressionItem.FormatSize(Items.Where(item => item.OutputPath != null)
            .Sum(item => item.OriginalBytes - (item.OutputBytes ?? item.OriginalBytes))));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isAdding;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _ffmpegAvailable;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private ImageCompressionItem? _selectedItem;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQualityMode))]
    [NotifyPropertyChangedFor(nameof(IsTargetMode))]
    private ImageCompressionMode _mode;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseCompressionType))]
    [NotifyPropertyChangedFor(nameof(IsLossy))]
    private ImageCompressionFormat _format = ImageCompressionFormat.Original;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLossyCompression))]
    [NotifyPropertyChangedFor(nameof(IsLossy))]
    private bool _lossless = true;
    [ObservableProperty] private int _quality = 80;
    [ObservableProperty] private int _targetPercent = 50;
    [ObservableProperty] private int _resizePercent = 100;
    [ObservableProperty] private int _maxDimension;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutputDirectory))]
    private string _outputDirectory = "";
    [ObservableProperty] private bool _skipLarger = true;

    partial void OnFormatChanged(ImageCompressionFormat value)
    {
        if (!IsLossy) Mode = ImageCompressionMode.Quality;
    }

    partial void OnLosslessChanged(bool value)
    {
        if (!IsLossy) Mode = ImageCompressionMode.Quality;
    }

    [RelayCommand]
    public void RefreshFfmpeg()
    {
        try
        {
            _ = Ffmpeg.Version;
            FfmpegAvailable = true;
            Message = "";
        }
        catch (Exception)
        {
            FfmpegAvailable = false;
            Message = Lang.Get("lang.kitopiaex.compression.ffmpeg_missing");
        }
    }

    public Task AddPathsAsync(IEnumerable<string> paths, byte[]? clipboardImage = null)
    {
        var previousImport = _pendingImport;
        return _pendingImport = ImportAsync();

        async Task ImportAsync()
        {
            await previousImport;
            if (StartCommand.ExecutionTask is { IsCompleted: false } compression) await compression;
            if (_disposed) return;
            IsAdding = true;
            Message = "";
            string? temporaryPath = null;
            try
            {
                if (clipboardImage != null)
                {
                    temporaryPath = Path.Combine(Path.GetTempPath(), $"KitopiaClipboard-{Guid.NewGuid():N}.png");
                    await File.WriteAllBytesAsync(temporaryPath, clipboardImage);
                }
                var files = await Task.Run(() => paths.SelectMany(path => Directory.Exists(path)
                    ? Directory.EnumerateFiles(path).Where(ImageCompressor.IsSupported)
                    : new[] { path }).Concat(temporaryPath != null ? [temporaryPath] : []).ToArray());
                foreach (var file in files)
                {
                    if (_disposed) break;
                    var path = Path.GetFullPath(file);
                    if (!ImageCompressor.IsSupported(path) || _paths.Contains(path)) continue;
                    try
                    {
                        var bytes = new FileInfo(path).Length;
                        var thumbnail = await Task.Run(() =>
                        {
                            try
                            {
                                using var input = File.OpenRead(path);
                                return Bitmap.DecodeToWidth(input, 96);
                            }
                            catch (Exception) { return null; }
                        });
                        if (_disposed) { thumbnail?.Dispose(); break; }
                        _paths.Add(path);
                        Items.Add(new ImageCompressionItem(path, bytes, thumbnail) { IsTemporary = path == temporaryPath });
                        if (path == temporaryPath) temporaryPath = null;
                    }
                    catch (Exception exception) { Message = exception.Message; }
                }
            }
            catch (Exception exception) { Message = exception.Message; }
            finally
            {
                if (temporaryPath != null) File.Delete(temporaryPath);
                IsAdding = false;
            }
        }
    }

    [RelayCommand]
    private void Remove(ImageCompressionItem? item)
    {
        if (!CanEdit || item == null) return;
        Items.Remove(item);
        if (SelectedItem == item) SelectedItem = null;
        _paths.Remove(item.SourcePath);
        item.Dispose();
    }

    [RelayCommand]
    private void Clear()
    {
        if (!CanEdit) return;
        foreach (var item in Items) item.Dispose();
        Items.Clear();
        SelectedItem = null;
        _paths.Clear();
        ProcessedCount = 0;
        ProgressText = "";
        Message = "";
    }

    [RelayCommand(CanExecute = nameof(CanStart), IncludeCancelCommand = true)]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = new ImageCompressionOptions
        {
            Mode = Mode, Format = Format, Lossless = Lossless, Quality = Quality, TargetPercent = TargetPercent,
            ResizePercent = ResizePercent, MaxDimension = MaxDimension, OutputDirectory = OutputDirectory,
            SkipLarger = SkipLarger
        };
        IsBusy = true;
        Message = "";
        ProcessedCount = 0;
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
                ProgressText = Lang.Format("lang.kitopiaex.compression.progress", ProcessedCount + 1, Items.Count, item.FileName);
                try
                {
                    var itemOptions = item.IsTemporary && string.IsNullOrWhiteSpace(options.OutputDirectory)
                        ? options with { OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") }
                        : options;
                    var result = await _compressor.CompressAsync(item.SourcePath, itemOptions, cancellationToken);
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
                ProcessedCount++;
                OnPropertyChanged(nameof(Summary));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var item in Items.Where(item => item.Status == ImageCompressionStatus.Waiting))
                    item.Status = ImageCompressionStatus.Cancelled;
                ProgressText = Lang.Get("lang.kitopiaex.compression.cancelled");
            }
            else ProgressText = Lang.Format("lang.kitopiaex.compression.finished", ProcessedCount, failed);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Summary));
            if (_disposed) foreach (var item in Items) item.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StartCancelCommand.Execute(null);
        StartCommand.NotifyCanExecuteChanged();
        if (!IsBusy) foreach (var item in Items) item.Dispose();
    }
}
