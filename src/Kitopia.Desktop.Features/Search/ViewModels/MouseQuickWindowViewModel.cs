using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Feature.Localization;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Search.Preview;
using Kitopia.Desktop.Features.Services.Config;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.Search.ViewModels;

public partial class MouseQuickWindowViewModel : ObservableObject, IDisposable
{
    private readonly record struct PreviewLocation(string Path, string? ArchiveSubPath);

    private IReadOnlyList<string> _files = Array.Empty<string>();
    private int _index;
    private PreviewLocation _currentLocation;
    private readonly Stack<PreviewLocation> _navigationStack = new();
    private readonly HashSet<string> _extractedTempDirectories = new(StringComparer.OrdinalIgnoreCase);
    private bool _skipArchiveExtractPromptFallback;
    private CancellationTokenSource? _previewCancellation;

    public string CurrentRootPath => _files.Count > 0 ? _files[_index] : "";

    [ObservableProperty] private string _fileName = Lang.Get("lang.kitopia.file_preview");
    [ObservableProperty] private string _fileDetails = "";
    [ObservableProperty] private string _selectedPath = "";
    [ObservableProperty] private string _positionLabel = "";
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private IReadOnlyList<FilePreviewEntry>? _entries;
    [ObservableProperty] private string? _nativePath;
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private string? _message = Lang.Get("lang.kitopia.select_a_file_in_explorer_and_use_the_preview_hotkey");
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _currentDirectoryPath;
    [ObservableProperty] private bool _canNavigateUp;
    [ObservableProperty] private bool _showNavigationBar;
    [ObservableProperty] private bool _isArchiveExtractPromptVisible;
    [ObservableProperty] private bool _dontAskArchiveExtractAgain;
    [ObservableProperty] private bool _showOpenFileButton;

    partial void OnMessageChanged(string? value) => UpdateOpenFileButtonVisibility();
    partial void OnSelectedPathChanged(string value) => UpdateOpenFileButtonVisibility();
    partial void OnIsArchiveExtractPromptVisibleChanged(bool value) => UpdateOpenFileButtonVisibility();

    private void UpdateOpenFileButtonVisibility() =>
        ShowOpenFileButton = !string.IsNullOrEmpty(Message)
                             && !IsArchiveExtractPromptVisible
                             && !string.IsNullOrEmpty(SelectedPath);

    public Task SetFilesAsync(IReadOnlyList<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _index = 0;
        return LoadCurrentFileAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private Task PreviousAsync()
    {
        _index--;
        return LoadCurrentFileAsync();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextAsync()
    {
        _index++;
        return LoadCurrentFileAsync();
    }

    private bool CanGoPrevious() => _index > 0;
    private bool CanGoNext() => _index + 1 < _files.Count;

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void OpenFile() => ServiceManager.Services.GetRequiredService<IDesktopShell>().Open(SelectedPath);

    [RelayCommand(CanExecute = nameof(HasFile))]
    private void RevealFile() => ServiceManager.Services.GetRequiredService<IDesktopShell>().OpenFolderAndSelect(SelectedPath);

    private bool HasFile() => _files.Count > 0;

    [RelayCommand]
    private Task OpenEntryAsync(FilePreviewEntry? entry)
    {
        if (entry is null) return Task.CompletedTask;
        if (entry.ArchiveEntryPath is not null)
        {
            if (entry.ArchiveEntryPath.StartsWith("../", StringComparison.Ordinal)
                || entry.ArchiveEntryPath.StartsWith("./", StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }
            var normalized = FilePreviewLoader.NormalizeArchivePath(entry.ArchiveEntryPath);
            if (normalized.Length == 0) return Task.CompletedTask;
            var archivePath = !string.IsNullOrWhiteSpace(entry.TargetPath) ? entry.TargetPath : _currentLocation.Path;
            _navigationStack.Push(_currentLocation);
            return LoadLocationAsync(new PreviewLocation(archivePath, normalized));
        }

        if (!string.IsNullOrWhiteSpace(entry.TargetPath))
        {
            _navigationStack.Push(_currentLocation);
            return LoadLocationAsync(new PreviewLocation(entry.TargetPath, null));
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ConfirmArchiveExtractAsync()
    {
        if (DontAskArchiveExtractAgain)
        {
            _skipArchiveExtractPromptFallback = true;
            if (ConfigManger.Configs.TryGetValue("KitopiaConfig", out var configBase)
                && configBase is KitopiaConfig config)
            {
                config.skipArchiveExtractPrompt = true;
                try { ConfigManger.Save(); } catch { /* ignore save failure in uninitialized test hosts */ }
            }
        }

        return LoadLocationAsync(_currentLocation, forceExtractArchiveFile: true);
    }

    [RelayCommand]
    private Task CancelArchiveExtractAsync() =>
        CanNavigateUp ? NavigateUpAsync() : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private Task NavigateUpAsync()
    {
        if (!TryGetParentLocation(out var parent)) return Task.CompletedTask;
        if (_navigationStack.Count > 0 && _navigationStack.Peek() == parent)
        {
            _navigationStack.Pop();
        }
        else if (_currentLocation.ArchiveSubPath is not { Length: > 0 } && _navigationStack.Count > 0)
        {
            parent = _navigationStack.Pop();
        }

        return LoadLocationAsync(parent);
    }

    private bool TryGetParentLocation(out PreviewLocation parent)
    {
        if (_currentLocation.ArchiveSubPath is { Length: > 0 } subPath)
        {
            var slashIndex = subPath.LastIndexOf('/');
            var parentSubPath = slashIndex >= 0 ? subPath[..slashIndex] : null;
            parent = new PreviewLocation(_currentLocation.Path, parentSubPath);
            return true;
        }

        if (_navigationStack.Count > 0)
        {
            parent = _navigationStack.Peek();
            return true;
        }

        parent = default;
        return false;
    }

    private bool ShouldSkipArchiveExtractPrompt() =>
        ConfigManger.Configs.TryGetValue("KitopiaConfig", out var configBase) && configBase is KitopiaConfig config
            ? config.skipArchiveExtractPrompt
            : _skipArchiveExtractPromptFallback;

    private void UpdateNavigationState(string? directoryPath)
    {
        CurrentDirectoryPath = directoryPath ?? (_navigationStack.Count > 0 ? _currentLocation.Path : null);
        CanNavigateUp = TryGetParentLocation(out _);
        ShowNavigationBar = Entries is not null || _navigationStack.Count > 0;
        NotifyCommandCanExecuteChanged(NavigateUpCommand);
    }

    private static void NotifyCommandCanExecuteChanged(IRelayCommand command)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            command.NotifyCanExecuteChanged();
            return;
        }

        Dispatcher.UIThread.Post(command.NotifyCanExecuteChanged);
    }

    private Task LoadCurrentFileAsync()
    {
        _navigationStack.Clear();
        CleanupExtractedTempFiles();
        var path = _files.Count > 0 ? _files[_index] : "";
        return LoadLocationAsync(new PreviewLocation(path, null));
    }

    private async Task LoadLocationAsync(PreviewLocation location, bool forceExtractArchiveFile = false)
    {
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        _currentLocation = location;
        var previousImage = PreviewImage;
        PreviewImage = null;
        previousImage?.Dispose();
        PreviewText = null;
        Entries = null;
        NativePath = null;
        Notice = null;
        Message = null;
        IsArchiveExtractPromptVisible = false;
        SelectedPath = location.Path;
        FileName = GetLocationDisplayName(location);
        FileDetails = "";
        PositionLabel = _files.Count > 0 ? $"{_index + 1} / {_files.Count}" : "";
        UpdateNavigationState(location.ArchiveSubPath is { Length: > 0 } sub
            ? $"{Path.GetFileName(location.Path)}/{sub}"
            : null);
        NotifyCommandCanExecuteChanged(PreviousCommand);
        NotifyCommandCanExecuteChanged(NextCommand);
        NotifyCommandCanExecuteChanged(OpenFileCommand);
        NotifyCommandCanExecuteChanged(RevealFileCommand);
        IsLoading = _files.Count > 0;
        try
        {
            if (_files.Count == 0)
            {
                Message = Lang.Get("lang.kitopia.select_a_file_in_explorer_and_use_the_preview_hotkey");
                return;
            }
            var extractArchiveFile = forceExtractArchiveFile || ShouldSkipArchiveExtractPrompt();
            var content = await FilePreviewLoader.LoadAsync(location.Path, location.ArchiveSubPath, extractArchiveFile, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                content.Image?.Dispose();
                if (content.ExtractedPath is not null && Path.GetDirectoryName(content.ExtractedPath) is { } cancelledDir)
                {
                    try { Directory.Delete(cancelledDir, recursive: true); } catch { /* ignore */ }
                }
                return;
            }
            if (content.ExtractedPath is { } extractedPath)
            {
                if (Path.GetDirectoryName(extractedPath) is { } tempDir)
                {
                    _extractedTempDirectories.Add(tempDir);
                }
                SelectedPath = extractedPath;
                if (content.Entries is not null)
                {
                    _currentLocation = new PreviewLocation(extractedPath, null);
                }
            }
            FileName = content.Name;
            FileDetails = content.Details;
            PreviewImage = content.Image;
            PreviewText = content.Text;
            Entries = content.Entries;
            IsLoading = false;
            NativePath = content.NativePath;
            Notice = content.Notice;
            if (content.RequiresArchiveExtraction)
            {
                DontAskArchiveExtractAgain = false;
                IsArchiveExtractPromptVisible = true;
                Message = Lang.Get("lang.kitopia.messages.archive_extract_prompt");
            }
            else if (content.Image is null && content.Text is null && content.Entries is null && content.NativePath is null)
            {
                Message = Lang.Get("lang.kitopia.preview_is_unavailable_for_this_file_type_use_open_file_to_view_it");
            }
            UpdateNavigationState(content.CurrentDirectoryPath);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                                        or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            if (!cancellation.IsCancellationRequested)
            {
                Message = Lang.Format("lang.kitopia.cannot_preview_this_file_value", exception.Message);
                UpdateNavigationState(CurrentDirectoryPath);
            }
        }
        finally
        {
            if (ReferenceEquals(_previewCancellation, cancellation))
            {
                if (NativePath is null) IsLoading = false;
                _previewCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private void CleanupExtractedTempFiles()
    {
        foreach (var directory in _extractedTempDirectories)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch
            {
                // Ignore best-effort cleanup errors for files still in use.
            }
        }
        _extractedTempDirectories.Clear();
    }

    private static string GetLocationDisplayName(PreviewLocation location)
    {
        if (string.IsNullOrEmpty(location.Path)) return Lang.Get("lang.kitopia.file_preview");
        if (location.ArchiveSubPath is { Length: > 0 } subPath) return Path.GetFileName(subPath);
        var name = Path.GetFileName(location.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? location.Path : name;
    }

    public void Dispose()
    {
        _previewCancellation?.Cancel();
        PreviewImage?.Dispose();
        PreviewImage = null;
        CleanupExtractedTempFiles();
    }
}
