using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitopiaEx.ImageCompression;
using PluginCore.Localization;
using PluginCore.Media;

namespace KitopiaEx.BatchRename;

public sealed partial class BatchRenameViewModel(Ffmpeg ffmpeg) : ObservableObject, IDisposable
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly StringComparer NameComparer = StringComparer.Create(CultureInfo.InvariantCulture,
        CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
    private readonly HashSet<string> _paths = new(PathComparer);
    private readonly ImageCompressor _compressor = new(ffmpeg);
    private Task _pendingImport = Task.CompletedTask;
    private bool _disposed;

    public ObservableCollection<BatchRenameItem> Items { get; } = [];
    public ImageCompressionFormat[] Formats { get; } =
        [ImageCompressionFormat.Original, ImageCompressionFormat.WebP, ImageCompressionFormat.JPEG, ImageCompressionFormat.PNG];
    public BatchRenameSortMode[] SortModes { get; } = Enum.GetValues<BatchRenameSortMode>();
    public bool IsSorted => SortMode != BatchRenameSortMode.Manual;
    public bool CanMoveUp => CanEdit && SelectedItem != null && Items.IndexOf(SelectedItem) > 0;
    public bool CanMoveDown => CanEdit && SelectedItem != null &&
        Items.IndexOf(SelectedItem) is var index && index >= 0 && index < Items.Count - 1;
    public bool IsEmpty => Items.Count == 0;
    public bool CanEdit => !_disposed && !IsBusy && !IsAdding;
    public bool CanStart => CanEdit && (!CompressImages || FfmpegAvailable) &&
        Items.Any(item => item.OutputPath == null) && Items.All(item => !item.HasError);
    public bool CanChooseLossless => Format is ImageCompressionFormat.PNG or ImageCompressionFormat.WebP ||
        Format == ImageCompressionFormat.Original && Items.Any(item =>
            Path.GetExtension(item.SourcePath).ToLowerInvariant() is ".png" or ".webp");
    public bool CanSetQuality => Format == ImageCompressionFormat.JPEG || !Lossless && CanChooseLossless ||
        Format == ImageCompressionFormat.Original && Items.Any(item =>
            Path.GetExtension(item.SourcePath).ToLowerInvariant() is ".jpg" or ".jpeg" or ".avif");
    public string Summary => Lang.Format("lang.kitopiaex.batch_rename.summary", Items.Count,
        Items.Count(item => item.IsCompleted), Items.Count(item => item.HasError));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isAdding;
    [ObservableProperty] private string _baseName = "";
    [ObservableProperty] private string _prefix = "";
    [ObservableProperty] private string _suffix = "";
    [ObservableProperty] private string _findText = "";
    [ObservableProperty] private string _replaceText = "";
    [ObservableProperty] private bool _keepOriginalName = true;
    [ObservableProperty] private bool _addNumber = true;
    [ObservableProperty] private int _startNumber = 1;
    [ObservableProperty] private int _numberDigits = 3;
    [ObservableProperty] private string _outputDirectory = "";
    [ObservableProperty] private bool _compressImages;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChooseLossless))]
    [NotifyPropertyChangedFor(nameof(CanSetQuality))]
    private ImageCompressionFormat _format = ImageCompressionFormat.Original;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetQuality))]
    private bool _lossless = true;
    [ObservableProperty] private int _quality = 80;
    [ObservableProperty] private int _resizePercent = 100;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _ffmpegAvailable;
    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private BatchRenameItem? _selectedItem;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSorted))]
    private BatchRenameSortMode _sortMode;
    [ObservableProperty] private bool _sortDescending;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(BaseName) or nameof(Prefix) or nameof(Suffix) or nameof(FindText) or
            nameof(ReplaceText) or nameof(KeepOriginalName) or nameof(AddNumber) or nameof(StartNumber) or nameof(NumberDigits) or
            nameof(OutputDirectory) or nameof(CompressImages) or nameof(Format))
            RefreshPreview();
        if (e.PropertyName is nameof(CanEdit) or nameof(SelectedItem))
        {
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnSortModeChanged(BatchRenameSortMode value) => ApplySort();
    partial void OnSortDescendingChanged(bool value) => ApplySort();

    partial void OnCompressImagesChanged(bool value)
    {
        Message = "";
        if (!value) return;
        try { _ = ffmpeg.Version; FfmpegAvailable = true; }
        catch (Exception)
        {
            FfmpegAvailable = false;
            Message = Lang.Get("lang.kitopiaex.compression.ffmpeg_missing");
        }
    }

    public Task AddPathsAsync(IEnumerable<string> paths)
    {
        var previousImport = _pendingImport;
        return _pendingImport = ImportAsync();

        async Task ImportAsync()
        {
            await previousImport;
            if (StartCommand.ExecutionTask is { IsCompleted: false } running) await running;
            if (_disposed) return;
            IsAdding = true;
            Message = "";
            try
            {
                foreach (var input in paths)
                {
                    if (_disposed) break;
                    try
                    {
                        var files = await Task.Run(() => Directory.Exists(input)
                            ? Directory.GetFiles(Path.GetFullPath(input)).Order(PathComparer).ToArray() : [Path.GetFullPath(input)]);
                        foreach (var file in files)
                        {
                            if (_disposed) break;
                            if (!File.Exists(file))
                            {
                                Message = Lang.Format("lang.kitopiaex.batch_rename.missing", file);
                                continue;
                            }
                            if (_paths.Add(file)) Items.Add(new BatchRenameItem(file));
                        }
                    }
                    catch (Exception exception) { Message = exception.Message; }
                }
            }
            finally
            {
                IsAdding = false;
                OnPropertyChanged(nameof(IsEmpty));
                ApplySort();
                RefreshPreview();
            }
        }
    }

    [RelayCommand]
    public void RefreshPreview()
    {
        if (IsBusy || IsAdding || _disposed) return;
        var destinations = new Dictionary<string, BatchRenameItem>(PathComparer);
        for (var index = 0; index < Items.Count; index++)
        {
            var item = Items[index];
            if (item.OutputPath != null) continue;
            item.Error = "";
            item.Status = BatchRenameStatus.Waiting;
            item.TargetPath = "";
            try
            {
                if (StartNumber < 0 || NumberDigits is < 1 or > 10)
                    throw new ArgumentOutOfRangeException(nameof(StartNumber));
                var name = string.IsNullOrEmpty(BaseName)
                    ? KeepOriginalName ? Path.GetFileNameWithoutExtension(item.SourcePath) : "" : BaseName;
                if (KeepOriginalName && !string.IsNullOrEmpty(FindText)) name = name.Replace(FindText, ReplaceText, StringComparison.Ordinal);
                var number = AddNumber
                    ? (name.Length > 0 ? "_" : "") + ((long)StartNumber + index).ToString("D" + NumberDigits, CultureInfo.InvariantCulture) : "";
                var compress = CompressImages && ImageCompressor.IsSupported(item.SourcePath);
                var extension = compress ? Format switch
                {
                    ImageCompressionFormat.WebP => ".webp",
                    ImageCompressionFormat.JPEG => ".jpg",
                    ImageCompressionFormat.PNG => ".png",
                    _ => Path.GetExtension(item.SourcePath).ToLowerInvariant()
                } : Path.GetExtension(item.SourcePath);
                var newName = Prefix + name + number + Suffix;
                item.NewName = newName + extension;
                var stem = item.NewName.Split('.')[0].ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(newName) || item.NewName.Length > 255 ||
                    item.NewName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || item.NewName is "." or ".." ||
                    item.NewName.EndsWith(' ') || item.NewName.EndsWith('.') ||
                    OperatingSystem.IsWindows() && (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                        stem.Length == 4 && stem[3] is >= '1' and <= '9' && (stem.StartsWith("COM") || stem.StartsWith("LPT"))))
                    throw new ArgumentException(Lang.Get("lang.kitopiaex.batch_rename.invalid_name"));
                var directory = string.IsNullOrWhiteSpace(OutputDirectory)
                    ? Path.GetDirectoryName(item.SourcePath)! : Path.GetFullPath(OutputDirectory);
                item.TargetPath = Path.Combine(directory, item.NewName);
                if (!File.Exists(item.SourcePath)) item.Error = Lang.Format("lang.kitopiaex.batch_rename.missing", item.SourcePath);
                else if (Directory.Exists(item.TargetPath) || File.Exists(item.TargetPath) &&
                         (compress || !PathComparer.Equals(item.SourcePath, item.TargetPath)))
                    item.Error = Lang.Get("lang.kitopiaex.batch_rename.conflict");
                if (!destinations.TryAdd(item.TargetPath, item))
                {
                    item.Error = Lang.Get("lang.kitopiaex.batch_rename.conflict");
                    destinations[item.TargetPath].Error = item.Error;
                }
            }
            catch (Exception exception) { item.Error = exception.Message; }
        }
        foreach (var item in Items.Where(item => item.OutputPath == null && item.HasError))
            item.Status = BatchRenameStatus.Failed;
        OnPropertyChanged(nameof(CanChooseLossless));
        OnPropertyChanged(nameof(CanSetQuality));
        OnPropertyChanged(nameof(Summary));
        StartCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void ApplySort()
    {
        if (!CanEdit || !IsSorted) return;
        try
        {
            var ordered = SortMode switch
            {
                BatchRenameSortMode.Name => Items.OrderBy(item => item.FileName, NameComparer),
                BatchRenameSortMode.Extension => Items.OrderBy(item => Path.GetExtension(item.SourcePath), NameComparer),
                BatchRenameSortMode.Size => Items.OrderBy(item => new FileInfo(item.OutputPath ?? item.SourcePath).Length),
                BatchRenameSortMode.ModifiedTime => Items.OrderBy(item => File.GetLastWriteTimeUtc(item.OutputPath ?? item.SourcePath)),
                BatchRenameSortMode.CreatedTime => Items.OrderBy(item => File.GetCreationTimeUtc(item.OutputPath ?? item.SourcePath)),
                _ => throw new ArgumentOutOfRangeException(nameof(SortMode))
            };
            if (SortMode != BatchRenameSortMode.Name)
                ordered = ordered.ThenBy(item => item.FileName, NameComparer);
            ordered = ordered.ThenBy(item => item.SourcePath, PathComparer);
            var sorted = (SortDescending ? ordered.Reverse() : ordered).ToArray();
            var selected = SelectedItem;
            for (var index = 0; index < sorted.Length; index++)
            {
                var currentIndex = Items.IndexOf(sorted[index]);
                if (currentIndex != index) Items.Move(currentIndex, index);
            }
            SelectedItem = selected;
            RefreshPreview();
        }
        catch (Exception exception) { Message = exception.Message; }
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp()
    {
        if (!CanMoveUp) return;
        var selected = SelectedItem!;
        var index = Items.IndexOf(selected);
        SortMode = BatchRenameSortMode.Manual;
        Items.Move(index, index - 1);
        SelectedItem = selected;
        RefreshPreview();
    }

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown()
    {
        if (!CanMoveDown) return;
        var selected = SelectedItem!;
        var index = Items.IndexOf(selected);
        SortMode = BatchRenameSortMode.Manual;
        Items.Move(index, index + 1);
        SelectedItem = selected;
        RefreshPreview();
    }

    [RelayCommand]
    private void Remove(BatchRenameItem? item)
    {
        if (!CanEdit || item == null) return;
        Items.Remove(item);
        _paths.Remove(item.SourcePath);
        if (SelectedItem == item) SelectedItem = null;
        OnPropertyChanged(nameof(IsEmpty));
        RefreshPreview();
    }

    [RelayCommand]
    private void Clear()
    {
        if (!CanEdit) return;
        Items.Clear();
        _paths.Clear();
        SelectedItem = null;
        ProcessedCount = 0;
        Message = "";
        ProgressText = "";
        OnPropertyChanged(nameof(IsEmpty));
        RefreshPreview();
    }

    [RelayCommand(CanExecute = nameof(CanStart), IncludeCancelCommand = true)]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        RefreshPreview();
        if (!CanStart) return;
        var pending = Items.Where(item => item.OutputPath == null).ToArray();
        var compressImages = CompressImages;
        var options = new ImageCompressionOptions
        {
            Format = Format, Lossless = Lossless, Quality = Quality, ResizePercent = ResizePercent
        };
        IsBusy = true;
        Message = "";
        ProcessedCount = 0;
        var failed = 0;
        try
        {
            foreach (var item in pending)
            {
                if (cancellationToken.IsCancellationRequested) break;
                item.Status = BatchRenameStatus.Running;
                ProgressText = Lang.Format("lang.kitopiaex.compression.progress", ProcessedCount + 1, pending.Length, item.FileName);
                string? compressedPath = null;
                try
                {
                    await Task.Run(async () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var compress = compressImages && ImageCompressor.IsSupported(item.SourcePath);
                        if (!compress && string.Equals(item.SourcePath, item.TargetPath, StringComparison.Ordinal)) return;
                        var directory = Path.GetDirectoryName(item.TargetPath)!;
                        Directory.CreateDirectory(directory);
                        if (compress)
                        {
                            var result = await _compressor.CompressAsync(item.SourcePath,
                                options with
                                {
                                    OutputDirectory = directory,
                                    SkipLarger = Path.GetExtension(item.SourcePath).Equals(Path.GetExtension(item.TargetPath),
                                        StringComparison.OrdinalIgnoreCase) && options.ResizePercent == 100
                                }, cancellationToken);
                            compressedPath = result.OutputPath;
                            cancellationToken.ThrowIfCancellationRequested();
                            // A larger encoding still needs the requested filename; retain the original pixels in that case.
                            if (compressedPath == null) File.Copy(item.SourcePath, item.TargetPath);
                            else
                            {
                                if (!string.Equals(compressedPath, item.TargetPath, StringComparison.Ordinal))
                                    File.Move(compressedPath, item.TargetPath);
                                compressedPath = null;
                            }
                        }
                        else File.Move(item.SourcePath, item.TargetPath);
                    }, cancellationToken);
                    item.OutputPath = item.TargetPath;
                    item.Status = !(compressImages && ImageCompressor.IsSupported(item.SourcePath)) &&
                        string.Equals(item.SourcePath, item.TargetPath, StringComparison.Ordinal)
                        ? BatchRenameStatus.Unchanged : BatchRenameStatus.Completed;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    item.Status = BatchRenameStatus.Cancelled;
                    break;
                }
                catch (Exception exception)
                {
                    item.Status = BatchRenameStatus.Failed;
                    item.Error = exception.Message;
                    failed++;
                }
                finally
                {
                    if (compressedPath != null && File.Exists(compressedPath))
                    {
                        try { File.Delete(compressedPath); }
                        catch (Exception exception) { Message = exception.Message; }
                    }
                }
                ProcessedCount++;
                OnPropertyChanged(nameof(Summary));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                foreach (var item in pending.Where(item => item.Status == BatchRenameStatus.Waiting))
                    item.Status = BatchRenameStatus.Cancelled;
                ProgressText = Lang.Get("lang.kitopiaex.compression.cancelled");
            }
            else ProgressText = Lang.Format("lang.kitopiaex.video_compression.finished", ProcessedCount, failed);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(Summary));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StartCancelCommand.Execute(null);
        StartCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));
    }
}
