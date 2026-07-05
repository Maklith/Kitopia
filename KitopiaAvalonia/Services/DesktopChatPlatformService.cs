using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Core.Services.DeviceCommunication.Platform;
using Core.ViewModel.Main;

namespace KitopiaAvalonia.Services;

public sealed class DesktopChatPlatformService : IChatPlatformService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<IReadOnlyList<string>> PickFilesToSendAsync()
    {
        var provider = MainWindow?.StorageProvider;
        if (provider is null) return [];
        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要发送的文件",
            AllowMultiple = true
        });
        return files.Select(f => f.Path.LocalPath)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    public async Task<ChatFileSaveTarget?> PickSaveTargetAsync(string suggestedFileName)
    {
        var provider = MainWindow?.StorageProvider;
        if (provider is null) return null;
        var extension = Path.GetExtension(suggestedFileName);
        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"保存文件: {suggestedFileName}",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.')
        });
        return file?.Path.LocalPath is { Length: > 0 } path
            ? ChatFileSaveTarget.FromLocalPath(path)
            : null;
    }

    public bool CanOpenFile => true;

    public void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = MainWindow?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(text);
    }

    public async Task<string?> PromptTextAsync(string title, string prompt, string? initialValue)
    {
        var owner = MainWindow;
        if (owner is null) return null;

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
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
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
        var window = MainWindow;
        var isActive = window is not null && window.IsVisible && window.IsActive;
        var isChatOpen = string.Equals(
            (window?.DataContext as MainWindowViewModel)?.Content as string,
            "device/chat",
            StringComparison.Ordinal);
        return new ChatDisplayContext(isActive, isChatOpen);
    }
}
