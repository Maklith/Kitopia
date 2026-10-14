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
using Kitopia.Feature.Localization;
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
    private readonly Dictionary<Guid, (string ConversationId, IToastProgressHandle Handle)> _incomingTransferToasts = [];
    private readonly DispatcherTimer _transferToastContextTimer = new(DispatcherPriority.Background, Dispatcher.UIThread)
        { Interval = TimeSpan.FromMilliseconds(300) };

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
        _transferToastContextTimer.Tick += (_, _) => CloseIncomingTransferToastsInCurrentConversation();
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
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NotifyIfNeeded(messageEvent));
            return;
        }

        try
        {
            CloseIncomingTransferToastsInCurrentConversation();
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
                    ShowDeviceChatToast(conversationId, displayName, Lang.Get("lang.kitopia.image_message"));
                    break;
                case FileTransferUpdatedEvent { Status: FileTransferStatus.WaitingForAccept } fileOffer:
                    ShowIncomingFileOfferToast(
                        conversationId,
                        displayName,
                        fileOffer.TransferId,
                        fileOffer.FileName,
                        fileOffer.TotalBytes);
                    break;
                case FileTransferUpdatedEvent downloadEvent
                    when (downloadEvent.Direction == FileTransferDirection.Download ||
                          _incomingTransferToasts.ContainsKey(downloadEvent.TransferId)) &&
                         downloadEvent.Status is FileTransferStatus.InProgress or
                        FileTransferStatus.Completed or
                        FileTransferStatus.Rejected or
                        FileTransferStatus.Failed or
                        FileTransferStatus.Cancelled or
                        FileTransferStatus.Timeout:
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
            Header = Lang.Format("lang.kitopia.messages.device_chat_value", displayName),
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
            Header = Lang.Format("lang.kitopia.messages.device_chat_value", displayName),
            Text = Lang.Format("lang.kitopia.messages.file_value_value", resolvedFileName, FormatFileSize(totalBytes ?? 0)),
            AutoCloseDelay = null,
            NotificationType = NotificationType.Information,
            CloseOnClick = true,
            ClickCallback = () => OpenConversationFromToast(conversationId),
            Actions =
            [
                new ToastAction
                {
                    Text = Lang.Get("lang.kitopia.agree"),
                    IsPrimary = true,
                    CloseOnClick = true,
                    Callback = () => _ = AcceptIncomingOfferFromToastAsync(
                        conversationId,
                        transferId,
                        resolvedFileName)
                },
                new ToastAction
                {
                    Text = Lang.Get("lang.kitopia.reject"),
                    CloseOnClick = true,
                    Callback = () => _ = RejectIncomingOfferFromToastAsync(conversationId, transferId)
                },
                new ToastAction
                {
                    Text = Lang.Get("lang.kitopia.open_chat"),
                    CloseOnClick = true,
                    Callback = () => OpenConversationFromToast(conversationId)
                }
            ]
        });
    }

    private async Task AcceptIncomingOfferFromToastAsync(
        string conversationId,
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
        }
        catch (Exception exception)
        {
            Logger.Warning(
                exception,
                "Accept incoming offer from desktop toast failed. ConversationId={ConversationId} TransferId={TransferId}",
                conversationId,
                transferId);
            _ = _toastService.Show(Lang.Get("lang.kitopia.device_chat"), Lang.Format("lang.kitopia.messages.unable_to_accept_transfer_value", exception.Message), NotificationType.Error);
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
            _ = _toastService.Show(Lang.Get("lang.kitopia.device_chat"), Lang.Format("lang.kitopia.messages.unable_to_reject_transfer_value", exception.Message), NotificationType.Error);
        }
    }

    private void ShowIncomingProgressToast(string conversationId, Guid transferId, string displayName, string fileName)
    {
        if (!_incomingTransferToasts.ContainsKey(transferId))
        {
            var handle = _toastService.ShowProgress(
                Lang.Format("lang.kitopia.messages.device_chat_value", displayName),
                Lang.Format("lang.kitopia.messages.receiving_value", fileName),
                NotificationType.Information,
                initialProgress: 0,
                isIndeterminate: false);
            _incomingTransferToasts.Add(transferId, (conversationId, handle));
            _transferToastContextTimer.Start();
        }
    }

    private void UpdateIncomingTransferToast(string displayName, FileTransferUpdatedEvent transferEvent)
    {
        _incomingTransferToasts.TryGetValue(transferEvent.TransferId, out var toast);
        var handle = toast.Handle;

        var fileName = string.IsNullOrWhiteSpace(transferEvent.FileName)
            ? transferEvent.TransferId.ToString("D")
            : transferEvent.FileName;

        switch (transferEvent.Status)
        {
            case FileTransferStatus.InProgress:
                if (handle is null)
                {
                    ShowIncomingProgressToast(transferEvent.ConversationId, transferEvent.TransferId, displayName, fileName);
                    _incomingTransferToasts.TryGetValue(transferEvent.TransferId, out toast);
                    handle = toast.Handle;
                }

                if (handle is not null)
                {
                    var progress = CalculateProgressPercent(
                        transferEvent.BytesTransferred,
                        transferEvent.TotalBytes);
                    handle.Update(
                        progress: progress,
                        text: Lang.Format("lang.kitopia.messages.receiving_value", fileName),
                        header: Lang.Format("lang.kitopia.messages.device_chat_value", displayName),
                        isIndeterminate: progress < 0);
                }

                break;
            case FileTransferStatus.Completed:
                handle?.Complete(
                    Lang.Format("lang.kitopia.messages.received_value", fileName),
                    Lang.Format("lang.kitopia.messages.device_chat_value", displayName),
                    TimeSpan.FromSeconds(4));
                RemoveIncomingTransferToast(transferEvent.TransferId);
                break;
            case FileTransferStatus.Rejected:
            case FileTransferStatus.Failed:
            case FileTransferStatus.Timeout:
                if (handle is not null)
                {
                    handle.Fail(
                        Lang.Format("lang.kitopia.messages.receive_failed_value", fileName),
                        Lang.Format("lang.kitopia.messages.device_chat_value", displayName),
                        TimeSpan.FromSeconds(5));
                }
                else
                {
                    ShowDeviceChatToast(
                        transferEvent.ConversationId,
                        displayName,
                        Lang.Format("lang.kitopia.messages.receive_failed_value", fileName));
                }

                RemoveIncomingTransferToast(transferEvent.TransferId);
                break;
            case FileTransferStatus.Cancelled:
                handle?.Close();
                RemoveIncomingTransferToast(transferEvent.TransferId);
                break;
        }
    }

    private void RemoveIncomingTransferToast(Guid transferId)
    {
        _incomingTransferToasts.Remove(transferId);
        if (_incomingTransferToasts.Count == 0)
        {
            _transferToastContextTimer.Stop();
        }
    }

    private void CloseIncomingTransferToastsInCurrentConversation()
    {
        if (_incomingTransferToasts.Count == 0) return;

        var messageAppService = _serviceProvider.GetRequiredService<IMessageAppService>();
        foreach (var (transferId, toast) in _incomingTransferToasts)
        {
            if (messageAppService.ResolveIncomingDisplayMode(toast.ConversationId) ==
                IncomingMessageDisplayMode.ShowInCurrentConversation)
            {
                toast.Handle.Close();
                RemoveIncomingTransferToast(transferId);
            }
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
            "rejected_by_peer" or "rejected_by_user" => Lang.Get("lang.kitopia.recipient_declined_the_file"),
            "timeout" => Lang.Get("lang.kitopia.file_send_timed_out_try_again_later"),
            _ => Lang.Get("lang.kitopia.failed_to_send_file")
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
