using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kitopia.Desktop.Abstractions.Shell;
using Kitopia.Desktop.Features.Search.Preview;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.Search.ViewModels;

public partial class MouseQuickWindowViewModel : ObservableObject, IDisposable
{
    private IReadOnlyList<string> _files = Array.Empty<string>();
    private int _index;
    private CancellationTokenSource? _previewCancellation;

    [ObservableProperty] private string _fileName = "文件速览";
    [ObservableProperty] private string _fileDetails = "";
    [ObservableProperty] private string _selectedPath = "";
    [ObservableProperty] private string _positionLabel = "";
    [ObservableProperty] private Bitmap? _previewImage;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private IReadOnlyList<FilePreviewEntry>? _entries;
    [ObservableProperty] private string? _nativePath;
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private string? _message = "在资源管理器中选中文件，使用已配置的速览快捷键预览。";
    [ObservableProperty] private bool _isLoading;

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

    private async Task LoadCurrentFileAsync()
    {
        _previewCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _previewCancellation = cancellation;
        var previousImage = PreviewImage;
        PreviewImage = null;
        previousImage?.Dispose();
        PreviewText = null;
        Entries = null;
        NativePath = null;
        Notice = null;
        Message = null;
        SelectedPath = _files.Count > 0 ? _files[_index] : "";
        FileName = _files.Count > 0 ? Path.GetFileName(SelectedPath) : "文件速览";
        FileDetails = "";
        PositionLabel = _files.Count > 0 ? $"{_index + 1} / {_files.Count}" : "";
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        OpenFileCommand.NotifyCanExecuteChanged();
        RevealFileCommand.NotifyCanExecuteChanged();
        IsLoading = _files.Count > 0;
        try
        {
            if (_files.Count == 0)
            {
                Message = "在资源管理器中选中文件，使用已配置的速览快捷键预览。";
                return;
            }
            var content = await FilePreviewLoader.LoadAsync(SelectedPath, cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                content.Image?.Dispose();
                return;
            }
            FileName = content.Name;
            FileDetails = content.Details;
            PreviewImage = content.Image;
            PreviewText = content.Text;
            Entries = content.Entries;
            IsLoading = false;
            NativePath = content.NativePath;
            Notice = content.Notice;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
                                        or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            if (!cancellation.IsCancellationRequested)
                Message = $"无法预览此文件：{exception.Message}";
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

    public void Dispose()
    {
        _previewCancellation?.Cancel();
        PreviewImage?.Dispose();
        PreviewImage = null;
    }
}
