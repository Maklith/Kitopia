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

namespace KitopiaEx.BatchRename;

public partial class BatchRenameWindow : UrsaWindow
{
    internal static BatchRenameWindow? Current { get; private set; }
    private BatchRenameViewModel ViewModel => (BatchRenameViewModel)DataContext!;

    public BatchRenameWindow() => InitializeComponent();

    [Feature("batch-rename", "lang.kitopiaex.batch_rename.title", "lang.kitopiaex.batch_rename.description",
        "lang.kitopia.files_and_devices", 0xf66a, 230)]
    public static void Open() => _ = OpenFilesAsync([]);

    public static async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                if (Current == null)
                {
                    Current = new BatchRenameWindow
                    {
                        DataContext = new BatchRenameViewModel(Kitopia.ServiceProvider.GetRequiredService<Ffmpeg>())
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
                else await Kitopia.IToastService.Show(Lang.Get("lang.kitopiaex.batch_rename.title"), exception.Message);
            }
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as BatchRenameViewModel)?.Dispose();
        if (Current == this) Current = null;
        base.OnClosed(e);
    }

    private async void AddFiles_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Lang.Get("lang.kitopiaex.batch_rename.add_files"), AllowMultiple = true,
                FileTypeFilter = [FilePickerFileTypes.All]
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
        e.Handled = true;
        if (!ViewModel.CanEdit) return;
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray() ?? [];
        await ViewModel.AddPathsAsync(paths);
    }

    private void OpenOutput_Click(object? sender, RoutedEventArgs e)
    {
        var item = ViewModel.SelectedItem ?? ViewModel.Items.FirstOrDefault();
        if (item == null) return;
        var directory = Path.GetDirectoryName(item.OutputPath ?? item.SourcePath)!;
        try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception exception) { ViewModel.Message = exception.Message; }
    }
}
