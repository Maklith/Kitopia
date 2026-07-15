using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Kitopia.Desktop.Features.ViewModel.Main;
using Kitopia.Feature.DeviceCommunication.Application;

namespace Kitopia.Desktop.Services;

public sealed class DesktopChatPlatformService : IChatPlatformService
{
    private static Window? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public bool CanOpenFile => true;

    public void OpenFile(string path)
    {
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
