using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Kitopia.Feature.Localization;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Search;
using Kitopia.Feature.DeviceCommunication.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Kitopia.Desktop.Services;

public sealed class DesktopChatAttachmentStore : IChatAttachmentStore
{
    private readonly IAppToolService? _appToolService;

    public DesktopChatAttachmentStore(IServiceProvider serviceProvider)
    {
        _appToolService = serviceProvider.GetService<IAppToolService>();
    }

    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<IReadOnlyList<string>> PickFilesToSendAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = MainWindow?.StorageProvider;
        if (provider is null)
        {
            return [];
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Lang.Get("lang.kitopia.select_files_to_send"),
            AllowMultiple = true
        });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    public async Task<ChatFileSaveTarget?> PickSaveTargetAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = MainWindow?.StorageProvider;
        if (provider is null)
        {
            return null;
        }

        var extension = Path.GetExtension(suggestedFileName);
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Lang.Format("lang.kitopia.messages.save_file_value", suggestedFileName),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.')
        });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path.LocalPath is { Length: > 0 } path
            ? ChatFileSaveTarget.FromLocalPath(path)
            : null;
    }

    public byte[]? GetFileIconPng(string path)
    {
        return _appToolService?.GetFileIconPng(path);
    }
}
