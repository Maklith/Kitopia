using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Core.Services.DeviceCommunication.Platform;

namespace Kitopia.Mobile.Services;

public sealed class MobileChatPlatformService : IChatPlatformService
{
    private readonly MobileTopLevelContext _topLevel;
    private readonly string _cacheDirectory;
    private readonly string _incomingRootDirectory;

    public MobileChatPlatformService(MobileTopLevelContext topLevel)
    {
        _topLevel = topLevel;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "Kitopia.Mobile", "picker-cache");
        _incomingRootDirectory = GetIncomingRootDirectory();
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<IReadOnlyList<string>> PickFilesToSendAsync()
    {
        var provider = _topLevel.CurrentTopLevel?.StorageProvider;
        if (provider?.CanOpen != true) return [];

        _topLevel.SuppressPause = true;
        try
        {
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的文件",
                AllowMultiple = false
            });
            var file = files.Count > 0 ? files[0] : null;
            if (file is null) return [];

            await using var source = await file.OpenReadAsync();
            var extension = Path.GetExtension(file.Name);
            var tempPath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid():N}{extension}");
            await using (var target = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await source.CopyToAsync(target);
            }
            return new List<string> { tempPath };
        }
        finally
        {
            _topLevel.SuppressPause = false;
        }
    }

    public async Task<ChatFileSaveTarget?> PickSaveTargetAsync(string suggestedFileName)
    {
        var provider = _topLevel.CurrentTopLevel?.StorageProvider;
        _topLevel.SuppressPause = true;
        try
        {
            if (provider?.CanSave == true)
            {
                var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "保存接收的文件",
                    SuggestedFileName = suggestedFileName
                });
                if (file?.Path is null)
                {
                    return null;
                }

                if (file.Path.IsFile)
                {
                    return ChatFileSaveTarget.FromLocalPath(file.Path.LocalPath);
                }

                return new ChatFileSaveTarget(
                    file.Path.ToString(),
                    null,
                    _ => new ValueTask<Stream>(file.OpenWriteAsync()));
            }

            return ChatFileSaveTarget.FromLocalPath(
                MobileReceiveSavePathResolver.ResolveIncomingPath(_incomingRootDirectory, suggestedFileName));
        }
        finally
        {
            _topLevel.SuppressPause = false;
        }
    }

    public bool CanOpenFile => false;

    public void OpenFile(string path) { }

    public async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = _topLevel.CurrentTopLevel?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }

    public async Task<string?> PromptTextAsync(string title, string prompt, string? initialValue)
    {
        if (_topLevel.CurrentTopLevel is not Window owner)
        {
            return null;
        }

        var textBox = new TextBox { Watermark = "备注名", Text = initialValue ?? string.Empty };
        string? result = null;
        var okButton = new Button { Content = "确定" };
        var cancelButton = new Button { Content = "取消" };
        var dialog = new Window
        {
            Title = title,
            Width = 320,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = prompt, FontSize = 14 },
                    textBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };
        okButton.Click += (_, _) => { result = textBox.Text; dialog.Close(); };
        cancelButton.Click += (_, _) => { result = null; dialog.Close(); };
        await dialog.ShowDialog(owner);
        return result;
    }

    public ChatDisplayContext GetDisplayContext(string? selectedConversationId)
    {
        _ = selectedConversationId;
        var isActive = _topLevel.IsActivityActive && _topLevel.CurrentTopLevel is not null;
        return new ChatDisplayContext(isActive, isActive);
    }

    private static string GetIncomingRootDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return Path.Combine(documents, "Kitopia");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(appData) ? Path.GetTempPath() : appData,
            "Kitopia");
    }
}
