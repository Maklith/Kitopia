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

namespace KitopiaEx.ImageCompression;

public partial class ImageCompressionWindow : UrsaWindow
{
    internal static ImageCompressionWindow? Current { get; private set; }
    private ImageCompressionViewModel ViewModel => (ImageCompressionViewModel)DataContext!;

    public ImageCompressionWindow()
    {
        InitializeComponent();
    }

    [Feature("image-compression", "lang.kitopiaex.compression.title", "lang.kitopiaex.compression.description",
        "lang.kitopia.screenshots_and_images", 0xEA14, 160)]
    public static void Open()
    {
        _ = OpenFilesAsync([]);
    }

    public static async Task OpenFilesAsync(IReadOnlyList<string> paths, byte[]? clipboardImage = null)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (Current == null)
                {
                    var host = Kitopia.ServiceProvider;
                    Current = new ImageCompressionWindow
                    {
                        DataContext = new ImageCompressionViewModel(host.GetRequiredService<Ffmpeg>())
                    };
                    Current.Show();
                }
                var window = Current;
                window.WindowState = Avalonia.Controls.WindowState.Normal;
                window.Activate();
                await window.ViewModel.AddPathsAsync(paths, clipboardImage);
            }
            catch (Exception exception)
            {
                if (Current != null) Current.ViewModel.Message = exception.Message;
                else await Kitopia.IToastService.Show(Lang.Get("lang.kitopiaex.compression.title"), exception.Message);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as ImageCompressionViewModel)?.Dispose();
        if (Current == this) Current = null;
        base.OnClosed(e);
    }

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Lang.Get("lang.kitopiaex.compression.add_images"), AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType(Lang.Get("lang.kitopiaex.image"))
                    { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp", "*.bmp", "*.avif"] }]
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
        e.DragEffects = ViewModel.CanEdit && e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Files_Drop(object? sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        e.Handled = true;
        await ViewModel.AddPathsAsync(paths);
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
