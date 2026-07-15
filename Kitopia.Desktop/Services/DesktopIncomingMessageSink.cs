using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Discovery;
using Kitopia.Feature.DeviceCommunication.Messages;
using Kitopia.Feature.DeviceCommunication.Messages.Chat;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using Serilog;

namespace Kitopia.Desktop.Services;

public sealed class DesktopIncomingMessageSink : IIncomingMessageSink
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DesktopIncomingMessageSink>();
    private readonly IncomingMessageBuffer _messageBuffer;
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IToastService _toastService;
    private readonly INavigationService _navigationService;
    private readonly IChatAttachmentStore _attachmentStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly object _transferToastSync = new();
    private readonly Dictionary<Guid, IToastProgressHandle> _incomingTransferToasts = [];

    public DesktopIncomingMessageSink(
        IncomingMessageBuffer messageBuffer,
        IDeviceDiscoveryService deviceDiscoveryService,
        IToastService toastService,
        INavigationService navigationService,
        IChatAttachmentStore attachmentStore,
        IServiceProvider serviceProvider)
    {
        _messageBuffer = messageBuffer;
        _deviceDiscoveryService = deviceDiscoveryService;
        _toastService = toastService;
        _navigationService = navigationService;
        _attachmentStore = attachmentStore;
        _serviceProvider = serviceProvider;
    }

    public async ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _messageBuffer.PublishAsync(message, cancellationToken);
        NotifyIfNeeded(DeviceMessageEventFactory.FromMessage(message));
    }

    public async ValueTask PublishEventAsync(
        DeviceMessageEvent messageEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageEvent);

        await _messageBuffer.PublishEventAsync(messageEvent, cancellationToken);
        NotifyIfNeeded(messageEvent);
    }

    private void NotifyIfNeeded(DeviceMessageEvent messageEvent)
    {
        try
        {
            var conversationId = messageEvent.ConversationId;
            var messageAppService = _serviceProvider.GetRequiredService<IMessageAppService>();
            if (string.IsNullOrWhiteSpace(conversationId) ||
                messageAppService.ResolveIncomingDisplayMode(conversationId) !=
                IncomingMessageDisplayMode.NotifyByToast)
            {
                return;
            }

            var displayName = ResolveConversationDisplayName(conversationId);
            switch (messageEvent)
            {
                case ChatMessageReceivedEvent { Message: TextChatMessage textMessage }:
                {
                    var text = textMessage.Text.Trim();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        ShowDeviceChatToast(conversationId, displayName, text);
                    }

                    break;
                }
                case ChatMessageReceivedEvent { Message: ImageChatMessage }:
                    ShowDeviceChatToast(conversationId, displayName, "[图片]");
                    break;
                case FileTransferUpdatedEvent { Status: FileTransferStatus.WaitingForAccept } fileOffer:
                    ShowIncomingFileOfferToast(
                        conversationId,
                        displayName,
                        fileOffer.TransferId,
                        fileOffer.FileName,
                        fileOffer.TotalBytes);
                    break;
                case FileTransferUpdatedEvent { Direction: FileTransferDirection.Download } downloadEvent
                    when downloadEvent.Status is FileTransferStatus.InProgress or
                        FileTransferStatus.Completed or
                        FileTransferStatus.Rejected or
                        FileTransferStatus.Failed:
                    UpdateIncomingTransferToast(displayName, downloadEvent);
                    break;
                case FileTransferUpdatedEvent { Direction: FileTransferDirection.Upload } uploadFailure
                    when uploadFailure.Status is FileTransferStatus.Rejected or
                        FileTransferStatus.Timeout or
                        FileTransferStatus.Failed:
                    if (!string.Equals(uploadFailure.Reason, "offer_not_received", StringComparison.Ordinal))
                    {
                        ShowDeviceChatToast(
                            conversationId,
                            displayName,
                            ResolveRejectToastText(uploadFailure.Reason));
                    }

                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "Failed to surface incoming desktop device message notification.");
        }
    }

    private void ShowDeviceChatToast(
        string conversationId,
        string displayName,
        string text,
        TimeSpan? autoCloseDelay = null)
    {
        _ = _toastService.Show(new ToastRequest
        {
            Header = $"设备聊天:{displayName}",
            Text = text,
            ClickCallback = () => OpenConversationFromToast(conversationId),
            AutoCloseDelay = autoCloseDelay ?? TimeSpan.FromSeconds(5)
        });
    }

    private void ShowIncomingFileOfferToast(
        string conversationId,
        string displayName,
        Guid transferId,
        string? fileName,
        long? totalBytes)
    {
        var resolvedFileName = string.IsNullOrWhiteSpace(fileName) ? transferId.ToString("D") : fileName;
        _ = _toastService.Show(new ToastRequest
        {
            Header = $"设备聊天:{displayName}",
            Text = $"文件: {resolvedFileName} ({FormatFileSize(totalBytes ?? 0)})",
            AutoCloseDelay = null,
            NotificationType = NotificationType.Information,
            CloseOnClick = true,
            ClickCallback = () => OpenConversationFromToast(conversationId),
            Actions =
            [
                new ToastAction
                {
                    Text = "同意",
                    IsPrimary = true,
                    CloseOnClick = true,
                    Callback = () => _ = AcceptIncomingOfferFromToastAsync(
                        conversationId,
                        displayName,
                        transferId,
                        resolvedFileName)
                },
                new ToastAction
                {
                    Text = "拒绝",
                    CloseOnClick = true,
                    Callback = () => _ = RejectIncomingOfferFromToastAsync(conversationId, transferId)
                },
                new ToastAction
                {
                    Text = "打开聊天",
                    CloseOnClick = true,
                    Callback = () => OpenConversationFromToast(conversationId)
                }
            ]
        });
    }

    private async Task AcceptIncomingOfferFromToastAsync(
        string conversationId,
        string displayName,
        Guid transferId,
        string fileName)
    {
        try
        {
            var saveTarget = await _attachmentStore.PickSaveTargetAsync(fileName);
            if (saveTarget is null)
            {
                return;
            }

            await _serviceProvider.GetRequiredService<IMessageAppService>().AcceptFileAsync(
                conversationId,
                transferId,
                saveTarget.DisplayPath,
                saveTarget.OpenWriteAsync);
            ShowIncomingProgressToast(transferId, displayName, fileName);
        }
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "Accept incoming offer from desktop toast failed. ConversationId={ConversationId} TransferId={TransferId}",
                conversationId,
                transferId);
            _ = _toastService.Show("设备聊天", $"同意接收失败: {exception.Message}", NotificationType.Error);
        }
    }

    private async Task RejectIncomingOfferFromToastAsync(string conversationId, Guid transferId)
    {
        try
        {
            await _serviceProvider.GetRequiredService<IMessageAppService>().RejectFileAsync(
                conversationId,
                transferId,
                "rejected_by_user");
        }
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "Reject incoming offer from desktop toast failed. ConversationId={ConversationId} TransferId={TransferId}",
                conversationId,
                transferId);
            _ = _toastService.Show("设备聊天", $"拒绝接收失败: {exception.Message}", NotificationType.Error);
        }
    }

    private void ShowIncomingProgressToast(Guid transferId, string displayName, string fileName)
    {
        lock (_transferToastSync)
        {
            if (_incomingTransferToasts.TryGetValue(transferId, out var existingHandle))
            {
                existingHandle.Update(
                    progress: 0,
                    text: $"接收中: {fileName}",
                    header: $"设备聊天:{displayName}",
                    isIndeterminate: false);
                return;
            }

            _incomingTransferToasts[transferId] = _toastService.ShowProgress(
                $"设备聊天:{displayName}",
                $"接收中: {fileName}",
                NotificationType.Information,
                initialProgress: 0,
                isIndeterminate: false);
        }
    }

    private void UpdateIncomingTransferToast(string displayName, FileTransferUpdatedEvent transferEvent)
    {
        IToastProgressHandle? handle;
        lock (_transferToastSync)
        {
            _incomingTransferToasts.TryGetValue(transferEvent.TransferId, out handle);
        }

        var fileName = string.IsNullOrWhiteSpace(transferEvent.FileName)
            ? transferEvent.TransferId.ToString("D")
            : transferEvent.FileName;

        switch (transferEvent.Status)
        {
            case FileTransferStatus.InProgress:
                if (handle is null)
                {
                    ShowIncomingProgressToast(transferEvent.TransferId, displayName, fileName);
                    lock (_transferToastSync)
                    {
                        _incomingTransferToasts.TryGetValue(transferEvent.TransferId, out handle);
                    }
                }

                if (handle is not null)
                {
                    var progress = CalculateProgressPercent(
                        transferEvent.BytesTransferred,
                        transferEvent.TotalBytes);
                    handle.Update(
                        progress: progress,
                        text: $"接收中: {fileName}",
                        header: $"设备聊天:{displayName}",
                        isIndeterminate: progress < 0);
                }

                break;
            case FileTransferStatus.Completed:
                handle?.Complete(
                    $"接收完成: {fileName}",
                    $"设备聊天:{displayName}",
                    TimeSpan.FromSeconds(4));
                RemoveIncomingTransferToast(transferEvent.TransferId);
                break;
            case FileTransferStatus.Rejected:
            case FileTransferStatus.Failed:
                if (handle is not null)
                {
                    handle.Fail(
                        $"接收失败: {fileName}",
                        $"设备聊天:{displayName}",
                        TimeSpan.FromSeconds(5));
                }
                else
                {
                    ShowDeviceChatToast(
                        transferEvent.ConversationId,
                        displayName,
                        $"接收失败: {fileName}");
                }

                RemoveIncomingTransferToast(transferEvent.TransferId);
                break;
        }
    }

    private void RemoveIncomingTransferToast(Guid transferId)
    {
        lock (_transferToastSync)
        {
            _incomingTransferToasts.Remove(transferId);
        }
    }

    private void OpenConversationFromToast(string conversationId)
    {
        _serviceProvider.GetRequiredService<IMessageAppService>().RequestOpenConversation(conversationId);
        Dispatcher.UIThread.Post(() =>
        {
            _navigationService.Navigate("device/chat");
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is null)
            {
                return;
            }

            desktop.MainWindow.Show();
            desktop.MainWindow.WindowState = WindowState.Normal;
            var platformHandle = desktop.MainWindow.TryGetPlatformHandle();
            if (platformHandle is not null)
            {
                _serviceProvider.GetService<IWindowTool>()?.SetForegroundWindow(platformHandle.Handle);
            }
        });
    }

    private string ResolveConversationDisplayName(string conversationId)
    {
        var device = _deviceDiscoveryService.Devices.FirstOrDefault(item =>
            string.Equals(item.Id, conversationId, StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(device?.DisplayName) ? conversationId : device.DisplayName;
    }

    private static string ResolveRejectToastText(string? reason)
    {
        return reason switch
        {
            "rejected_by_peer" or "rejected_by_user" => "对方已拒绝接收文件",
            "timeout" => "文件发送超时，请稍后重试",
            _ => "文件发送失败"
        };
    }

    private static double CalculateProgressPercent(long? bytesTransferred, long? totalBytes)
    {
        if (!bytesTransferred.HasValue || !totalBytes.HasValue || totalBytes.Value <= 0)
        {
            return -1;
        }

        return Math.Clamp((double)bytesTransferred.Value / totalBytes.Value, 0d, 1d) * 100d;
    }

    private static string FormatFileSize(long bytes)
    {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = Math.Max(0d, bytes);
        var index = 0;
        while (value >= 1024d && index < units.Length - 1)
        {
            value /= 1024d;
            index++;
        }

        return value >= 100d ? $"{value:0} {units[index]}" : $"{value:0.0} {units[index]}";
    }
}
