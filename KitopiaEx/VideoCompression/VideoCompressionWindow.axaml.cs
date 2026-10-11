using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Localization;
using PluginCore.Media;
using Ursa.Controls;

namespace KitopiaEx.VideoCompression;

public partial class VideoCompressionWindow : UrsaWindow
{
    internal static VideoCompressionWindow? Current { get; private set; }
    private VideoCompressionViewModel ViewModel => (VideoCompressionViewModel)DataContext!;

    public VideoCompressionWindow() => InitializeComponent();

    [Feature("video-compression", "lang.kitopiaex.video_compression.title", "lang.kitopiaex.video_compression.description",
        "lang.kitopia.screenshots_and_images", 0xF850, 170)]
    public static void Open() => _ = OpenFilesAsync([]);

    public static async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (Current == null)
                {
                    Current = new VideoCompressionWindow
                    {
                        DataContext = new VideoCompressionViewModel(Kitopia.ServiceProvider.GetRequiredService<Ffmpeg>())
                    };
                    Current.Show();
                }
                var window = Current;
                window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Activate();
                await window.ViewModel.AddPathsAsync(paths);
            }
            catch (Exception exception)
            {
                if (Current != null) Current.ViewModel.Message = exception.Message;
                else await Kitopia.IToastService.Show(Lang.Get("lang.kitopiaex.video_compression.title"), exception.Message);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as VideoCompressionViewModel)?.Dispose();
        if (Current == this) Current = null;
        base.OnClosed(e);
    }

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Lang.Get("lang.kitopiaex.video_compression.add_videos"),
                AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType(Lang.Get("lang.kitopiaex.video_compression.file"))
                    { Patterns = ["*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.m4v", "*.wmv", "*.flv", "*.ts", "*.mts", "*.m2ts"] }]
            });
            await ViewModel.AddPathsAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception exception) { ViewModel.Message = exception.Message; }
    }

    private async void AddFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = Lang.Get("lang.kitopiaex.compression.add_folder"), AllowMultiple = true });
            await ViewModel.AddPathsAsync(folders.Select(folder => folder.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception exception) { ViewModel.Message = exception.Message; }
    }

    private async void OutputFolder_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            { Title = Lang.Get("lang.kitopiaex.compression.choose_directory") });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) ViewModel.OutputDirectory = path;
        }
        catch (Exception exception) { ViewModel.Message = exception.Message; }
    }

    private void Files_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ViewModel.CanEdit && e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Files_Drop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        await ViewModel.AddPathsAsync(e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>() ?? []);
    }

    private void OpenOutput_Click(object? sender, RoutedEventArgs e)
    {
        var path = ViewModel.SelectedItem?.OutputPath ?? ViewModel.Items.FirstOrDefault(item => item.OutputPath != null)?.OutputPath;
        if (path == null && string.IsNullOrWhiteSpace(ViewModel.OutputDirectory))
            path = ViewModel.SelectedItem?.SourcePath ?? ViewModel.Items.FirstOrDefault()?.SourcePath;
        var directory = path != null ? Path.GetDirectoryName(path) : ViewModel.OutputDirectory;
        if (string.IsNullOrWhiteSpace(directory)) return;
        try { Process.Start(new ProcessStartInfo(Path.GetFullPath(directory)) { UseShellExecute = true }); }
        catch (Exception exception) { ViewModel.Message = exception.Message; }
    }
}
