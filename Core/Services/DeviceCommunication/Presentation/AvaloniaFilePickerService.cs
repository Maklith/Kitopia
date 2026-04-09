using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Core.Services.DeviceCommunication.Presentation;

public sealed class AvaloniaFilePickerService : IFilePickerService
{
    public async Task<string?> PickFileToSendAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                return null;
            }

            var files = await lifetime.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的文件",
                AllowMultiple = false
            });

            return files is { Count: > 0 } ? files[0].Path.LocalPath : null;
        });
    }

    public async Task<string?> PickImageToSendAsync()
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                return null;
            }

            var files = await lifetime.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的图片",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("图片文件")
                    {
                        Patterns =
                        [
                            "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif",
                            "*.webp", "*.tif", "*.tiff", "*.ico", "*.heic", "*.heif"
                        ]
                    }
                ]
            });

            return files is { Count: > 0 } ? files[0].Path.LocalPath : null;
        });
    }

    public async Task<string?> PickSaveFilePathAsync(string title, string suggestedFileName)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lifetime = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (lifetime?.MainWindow == null)
            {
                return null;
            }

            var file = await lifetime.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName
            });

            return file?.Path.LocalPath;
        });
    }
}
