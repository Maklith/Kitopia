using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Core.Services;
using Core.Services.DeviceCommunication;
using Core.Services.DeviceCommunication.Application;
using Core.Services.DeviceCommunication.Discovery;
using Core.Services.DeviceCommunication.Messages.Chat;
using Core.Services.DeviceCommunication.Routing;
using Core.ViewModel.Main;
using OpenCvSharp;
using PluginCore;
using Serilog;
using Serilog.Core;

namespace Core.ViewModel.Pages.device;

public partial class DeviceCommunicationPageViewModel : ObservableObject, IDisposable
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceCommunicationPageViewModel>();
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IMessageAppService _messageAppService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly Task _receiveTask;
    private readonly DispatcherTimer _displayContextSyncTimer;
    private readonly Dictionary<string, DeviceConversationItem> _conversationLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceModel> _trackedDevices = new(StringComparer.Ordinal);
    private readonly ObservableCollection<DeviceChatMessageItem> _emptyMessages = [];
    private bool _disposed;

    public ObservableCollection<DeviceConversationItem> Conversations { get; } = [];

    public ObservableCollection<DeviceChatMessageItem> CurrentMessages =>
        SelectedConversation?.Messages ?? _emptyMessages;

    public string CurrentConversationTitle => SelectedConversation?.DisplayName ?? "Device Chat";

    public string CurrentConversationSubtitle => SelectedConversation is null
        ? "Select a device to start chatting"
        : $"{SelectedConversation.StatusText} - {SelectedConversation.AddressText}";

    public bool HasConversationSelected => SelectedConversation is not null;
    public bool ShowConversationPlaceholder => !HasConversationSelected;
    public bool HasConversations => Conversations.Count > 0;
    public bool HasNoConversations => !HasConversations;

    public DeviceCommunicationPageViewModel(
        IDeviceDiscoveryService deviceDiscoveryService,
        IMessageAppService messageAppService,
        IClipboardService clipboardService,
        IToastService toastService)
    {
        _deviceDiscoveryService = deviceDiscoveryService;
        _messageAppService = messageAppService;
        _clipboardService = clipboardService;
        _toastService = toastService;
        _displayContextSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _displayContextSyncTimer.Tick += (_, _) => SyncDisplayContext();

        _deviceDiscoveryService.Devices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
        _receiveTask = RunReceiveLoopAsync(_receiveCancellation.Token);
        SyncDisplayContext();
        _displayContextSyncTimer.Start();
        SyncConversationsFromDiscovery();
    }

    [ObservableProperty]
    private DeviceConversationItem? _selectedConversation;

    [ObservableProperty]
    private string _messageText = string.Empty;

    [ObservableProperty]
    private bool _isSending;

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        var conversation = SelectedConversation;
        var text = MessageText.Trim();
        if (conversation is null || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var message = new DeviceChatMessageItem(text, isOutgoing: true, DateTimeOffset.Now)
        {
            IsPending = true
        };

        conversation.Messages.Add(message);
        conversation.SetLastMessage(text, message.Timestamp);
        conversation.UnreadCount = 0;
        MessageText = string.Empty;
        SortConversations();

        IsSending = true;
        try
        {
            await SendToConversationAsync(conversation, text);
            message.IsPending = false;
            message.IsFailed = false;
        }
        catch (Exception ex)
        {
            message.IsPending = false;
            message.IsFailed = true;
            Logger.Warning(ex, "Send chat message failed. DeviceId={DeviceId}", conversation.DeviceId);
            _toastService.Show("Device Chat", $"Send failed: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanSendMessage()
    {
        return !IsSending &&
               SelectedConversation is not null &&
               !string.IsNullOrWhiteSpace(MessageText);
    }

    private bool CanOperateConversation()
    {
        return !IsSending && SelectedConversation is not null;
    }

    private async Task SendToConversationAsync(DeviceConversationItem conversation, string text)
    {
        if (string.IsNullOrWhiteSpace(conversation.DeviceId))
        {
            throw new InvalidOperationException("Invalid target device identity.");
        }

        var protocol = conversation.SupportQuic && conversation.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
        var port = protocol == LocalDataTransportProtocol.Quic ? conversation.QuicPort : conversation.TcpPort;

        if (port <= 0 || conversation.Address == IPAddress.None)
        {
            throw new InvalidOperationException("Invalid target address or port.");
        }

        try
        {
            await SendMessageCoreAsync(conversation, text, protocol, port);
        }
        catch (Exception ex) when (protocol == LocalDataTransportProtocol.Quic && conversation.TcpPort > 0)
        {
            Logger.Warning(ex, "Send chat message over QUIC failed, fallback to TCP. DeviceId={DeviceId}",
                conversation.DeviceId);
            await SendMessageCoreAsync(conversation, text, LocalDataTransportProtocol.Tcp, conversation.TcpPort);
        }
    }

    private async Task SendImageToConversationAsync(DeviceConversationItem conversation, ImageChatMessage message,
        Stream stream)
    {
        var protocol = conversation.SupportQuic && conversation.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
        var port = protocol == LocalDataTransportProtocol.Quic ? conversation.QuicPort : conversation.TcpPort;

        try
        {
            await SendImageCoreAsync(conversation, message, stream, protocol, port);
        }
        catch (Exception ex) when (protocol == LocalDataTransportProtocol.Quic && conversation.TcpPort > 0)
        {
            Logger.Warning(ex, "Send image over QUIC failed, fallback to TCP. DeviceId={DeviceId}", conversation.DeviceId);
            await SendImageCoreAsync(conversation, message, stream, LocalDataTransportProtocol.Tcp, conversation.TcpPort);
        }
    }

    private async Task SendFileToConversationAsync(DeviceConversationItem conversation, FileChatMessage message,
        Stream stream)
    {
        var protocol = conversation.SupportQuic && conversation.QuicPort > 0
            ? LocalDataTransportProtocol.Quic
            : LocalDataTransportProtocol.Tcp;
        var port = protocol == LocalDataTransportProtocol.Quic ? conversation.QuicPort : conversation.TcpPort;

        try
        {
            await SendFileCoreAsync(conversation, message, stream, protocol, port);
        }
        catch (Exception ex) when (protocol == LocalDataTransportProtocol.Quic && conversation.TcpPort > 0)
        {
            Logger.Warning(ex, "Send file over QUIC failed, fallback to TCP. DeviceId={DeviceId}", conversation.DeviceId);
            await SendFileCoreAsync(conversation, message, stream, LocalDataTransportProtocol.Tcp, conversation.TcpPort);
        }
    }

    private async Task SendMessageCoreAsync(
        DeviceConversationItem conversation,
        string text,
        LocalDataTransportProtocol protocol,
        int port)
    {
        var sendContext = BuildContext(conversation, protocol, port);

        await _messageAppService.SendTextChatAsync(sendContext, new TextChatMessage(conversation.DeviceId, text));
    }

    private async Task SendImageCoreAsync(
        DeviceConversationItem conversation,
        ImageChatMessage message,
        Stream stream,
        LocalDataTransportProtocol protocol,
        int port)
    {
        var sendContext = BuildContext(conversation, protocol, port);
        await _messageAppService.SendImageChatAsync(sendContext, message, stream);
    }

    private async Task SendFileCoreAsync(
        DeviceConversationItem conversation,
        FileChatMessage message,
        Stream stream,
        LocalDataTransportProtocol protocol,
        int port)
    {
        var sendContext = BuildContext(conversation, protocol, port);
        await _messageAppService.SendFileChatAsync(sendContext, message, stream);
    }

    private static MessageContext BuildContext(DeviceConversationItem conversation, LocalDataTransportProtocol protocol,
        int port)
    {
        var remoteEndPoint = new IPEndPoint(conversation.Address, port);
        return new MessageContext(protocol, remoteEndPoint, conversation.DeviceId);
    }

    private async Task RunReceiveLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var evt in _messageAppService.ReceiveAsync(token))
            {
                if (evt.EventType == IncomingMessageEventType.TransferProgress && evt.Message is FileChatMessage fileMessage)
                {
                    OnFileTransferProgress(fileMessage, evt.BytesTransferred, evt.TotalBytes, DateTimeOffset.UtcNow);
                    continue;
                }

                switch (evt.Message)
                {
                    case TextChatMessage chatMessage:
                        OnChatMessageReceived(chatMessage, DateTimeOffset.UtcNow);
                        break;
                    case ImageChatMessage imageMessage:
                        OnImageMessageReceived(imageMessage, evt.PayloadBytes, DateTimeOffset.UtcNow);
                        break;
                    case FileOfferChatMessage offerMessage:
                        OnFileOfferReceived(offerMessage);
                        break;
                    case FileCompleteChatMessage fileCompleteMessage:
                        OnFileTransferCompleted(fileCompleteMessage, DateTimeOffset.UtcNow);
                        break;
                    case FileRejectChatMessage fileRejectMessage:
                        OnFileTransferRejected(fileRejectMessage, DateTimeOffset.UtcNow);
                        break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Message receive loop failed.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateConversation))]
    private async Task PasteSendAsync()
    {
        var conversation = SelectedConversation;
        if (conversation is null)
        {
            return;
        }

        var errors = new List<string>();

        if (_clipboardService.HasText())
        {
            var text = _clipboardService.GetText()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    await SendToConversationAsync(conversation, text);
                }
                catch (Exception ex)
                {
                    errors.Add($"text:{ex.Message}");
                }
            }
        }

        if (_clipboardService.HasImage())
        {
            try
            {
                using var image = _clipboardService.GetImage();
                if (image is not null && !image.Empty())
                {
                    Cv2.ImEncode(".png", image, out var imageBytes);
                    var imageBubble = DeviceChatMessageItem.CreateImage(imageBytes, isOutgoing: true, DateTimeOffset.Now);
                    imageBubble.IsPending = true;
                    conversation.Messages.Add(imageBubble);
                    conversation.SetLastMessage("[Image]", imageBubble.Timestamp);
                    SortConversations();

                    await using var imageStream = new MemoryStream(imageBytes, writable: false);
                    var imageMessage = new ImageChatMessage(conversation.DeviceId, Guid.NewGuid(), imageBytes.LongLength,
                        "image/png", false);
                    try
                    {
                        await SendImageToConversationAsync(conversation, imageMessage, imageStream);
                        imageBubble.IsPending = false;
                        imageBubble.IsFailed = false;
                    }
                    catch
                    {
                        imageBubble.IsPending = false;
                        imageBubble.IsFailed = true;
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"image:{ex.Message}");
            }
        }

        if (_clipboardService.HasFiles())
        {
            foreach (var filePath in _clipboardService.GetFiles())
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        continue;
                    }

                    var fileInfo = new FileInfo(filePath);
                    var fileMessage = new FileChatMessage(
                        conversation.DeviceId,
                        Guid.NewGuid(),
                        fileInfo.Name,
                        fileInfo.Length);
                    var fileBubble = DeviceChatMessageItem.CreateFile(fileInfo.Name, fileInfo.Length, isOutgoing: true,
                        DateTimeOffset.Now);
                    fileBubble.TrackingTransferId = fileMessage.ChannelId;
                    fileBubble.IsReceiving = true;
                    fileBubble.ReceiveProgress = 0d;
                    fileBubble.IsPending = true;
                    conversation.Messages.Add(fileBubble);
                    conversation.SetLastMessage($"[File] {fileInfo.Name}", fileBubble.Timestamp);
                    SortConversations();

                    await using var fileStream = File.OpenRead(filePath);
                    try
                    {
                        await SendFileToConversationAsync(conversation, fileMessage, fileStream);
                        fileBubble.IsReceiving = false;
                        fileBubble.ReceiveProgress = 1d;
                        fileBubble.IsPending = false;
                        fileBubble.IsFailed = false;
                    }
                    catch
                    {
                        fileBubble.IsReceiving = false;
                        fileBubble.IsPending = false;
                        fileBubble.IsFailed = true;
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"file:{Path.GetFileName(filePath)}:{ex.Message}");
                }
            }
        }

        if (errors.Count > 0)
        {
            _toastService.Show("Device Chat", $"Paste send partial failed: {string.Join(";", errors)}",
                NotificationType.Warning);
        }
    }

    [RelayCommand]
    private async Task AcceptIncomingOfferAsync(DeviceChatMessageItem? messageItem)
    {
        if (messageItem is not IncomingFileOfferChatMessageItem offer || offer.IsHandled)
        {
            return;
        }

        var conversation = Conversations.FirstOrDefault(c => c.DeviceId == offer.ConversationId);
        if (conversation is null)
        {
            return;
        }

        try
        {
            var savePath = await PickSavePathAsync(offer.FileName);
            if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathRooted(savePath))
            {
                return;
            }

            await SendOfferDecisionWithFallbackAsync(
                conversation,
                offer,
                async context => await _messageAppService.AcceptFileAsync(context, offer.TransferId, savePath));
            offer.IsHandled = true;
            offer.IsReceiving = true;
            offer.ReceiveProgress = 0d;
            offer.Text = $"[File] Receiving {offer.FileName}";
            conversation.SetLastMessage($"[File] {offer.FileName}", DateTimeOffset.Now);
            SortConversations();
        }
        catch (Exception ex)
        {
            _toastService.Show("Device Chat", $"Accept failed: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task RejectIncomingOfferAsync(DeviceChatMessageItem? messageItem)
    {
        if (messageItem is not IncomingFileOfferChatMessageItem offer || offer.IsHandled)
        {
            return;
        }

        var conversation = Conversations.FirstOrDefault(c => c.DeviceId == offer.ConversationId);
        if (conversation is null)
        {
            return;
        }

        try
        {
            await SendOfferDecisionWithFallbackAsync(
                conversation,
                offer,
                async context => await _messageAppService.RejectFileAsync(context, offer.TransferId, "rejected_by_user"));
            offer.IsHandled = true;
            offer.Text = $"[File] Rejected {offer.FileName}";
            conversation.SetLastMessage($"[File] {offer.FileName}", DateTimeOffset.Now);
            SortConversations();
        }
        catch (Exception ex)
        {
            _toastService.Show("Device Chat", $"Reject failed: {ex.Message}", NotificationType.Error);
        }
    }

    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(SyncConversationsFromDiscovery);
    }

    private void OnTrackedDevicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed || sender is not DeviceModel device)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            UpsertConversation(device);
            OnPropertyChanged(nameof(CurrentConversationTitle));
            OnPropertyChanged(nameof(CurrentConversationSubtitle));
            SortConversations();
        });
    }

    private void OnChatMessageReceived(TextChatMessage message, DateTimeOffset timestampUtc)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_conversationLookup.TryGetValue(message.ConversationId, out var conversation))
            {
                conversation = Conversations.FirstOrDefault(c => c.DeviceId == message.ConversationId);
            }
            if (conversation is null)
            {
                Logger.Debug(
                    "Drop chat message because sender device is not discovered. DeviceId={DeviceId}",
                    message.ConversationId);
                return;
            }

            var text = message.Text.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var timestamp = timestampUtc.ToLocalTime();
            conversation.Messages.Add(new DeviceChatMessageItem(text, isOutgoing: false, timestamp));
            conversation.SetLastMessage(text, timestamp);

            if (!IsForegroundCurrentConversation(conversation))
            {
                conversation.UnreadCount++;
            }
            else
            {
                conversation.UnreadCount = 0;
            }

            SortConversations();
        });
    }

    private void OnImageMessageReceived(ImageChatMessage message, byte[]? payloadBytes, DateTimeOffset timestampUtc)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_conversationLookup.TryGetValue(message.ConversationId, out var conversation))
            {
                conversation = Conversations.FirstOrDefault(c => c.DeviceId == message.ConversationId);
            }

            if (conversation is null)
            {
                Logger.Debug("Drop image message because sender device is not discovered. DeviceId={DeviceId}",
                    message.ConversationId);
                return;
            }

            var timestamp = timestampUtc.ToLocalTime();
            var imageItem = DeviceChatMessageItem.CreateImage(payloadBytes, isOutgoing: false, timestamp);
            if (imageItem.ImagePreview is null)
            {
                imageItem.Text = $"[Image] {Math.Max(1, message.SizeBytes / 1024)} KB";
            }

            conversation.Messages.Add(imageItem);
            conversation.SetLastMessage("[Image]", timestamp);

            if (!IsForegroundCurrentConversation(conversation))
            {
                conversation.UnreadCount++;
            }
            else
            {
                conversation.UnreadCount = 0;
            }

            SortConversations();
        });
    }

    private void OnFileOfferReceived(FileOfferChatMessage message)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            var senderId = message.ConversationId;
            if (!_conversationLookup.TryGetValue(senderId, out var conversation))
            {
                conversation = Conversations.FirstOrDefault(c => c.DeviceId == senderId);
            }

            if (conversation is null)
            {
                Logger.Debug("Drop file offer because sender is unknown. SenderId={SenderId}", senderId);
                return;
            }

            var protocol = conversation.SupportQuic && conversation.QuicPort > 0
                ? LocalDataTransportProtocol.Quic
                : LocalDataTransportProtocol.Tcp;
            var port = protocol == LocalDataTransportProtocol.Quic ? conversation.QuicPort : conversation.TcpPort;

            var timestamp = DateTimeOffset.Now;
            var offerMessage = new IncomingFileOfferChatMessageItem(
                message.ConversationId,
                message.TransferId,
                message.FileName,
                message.SizeBytes,
                protocol,
                port,
                timestamp);
            offerMessage.TrackingTransferId = message.TransferId;
            conversation.Messages.Add(offerMessage);
            conversation.SetLastMessage($"[File] {message.FileName}", timestamp);

            if (!IsForegroundCurrentConversation(conversation))
            {
                conversation.UnreadCount++;
            }
            SortConversations();
        });
    }

    private void OnFileTransferCompleted(FileCompleteChatMessage message, DateTimeOffset timestampUtc)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_conversationLookup.TryGetValue(message.ConversationId, out var conversation))
            {
                conversation = Conversations.FirstOrDefault(c => c.DeviceId == message.ConversationId);
            }

            if (conversation is null)
            {
                return;
            }

            var offerItem = conversation.Messages
                .OfType<IncomingFileOfferChatMessageItem>()
                .FirstOrDefault(item => item.TransferId == message.TransferId);
            if (offerItem is not null)
            {
                offerItem.ReceiveProgress = 1d;
                offerItem.IsReceiving = false;
                offerItem.Text = $"[File] Received {offerItem.FileName}";
                offerItem.IsHandled = true;
                offerItem.IsPending = false;
            }
            else
            {
                var outgoingItem = conversation.Messages
                    .FirstOrDefault(item => item.IsOutgoing && item.TrackingTransferId == message.TransferId);
                if (outgoingItem is not null)
                {
                    outgoingItem.ReceiveProgress = 1d;
                    outgoingItem.IsReceiving = false;
                    outgoingItem.IsPending = false;
                    outgoingItem.IsFailed = false;
                }
            }

            var timestamp = timestampUtc.ToLocalTime();
            conversation.SetLastMessage("[File] Received", timestamp);
            SortConversations();
        });
    }

    private void OnFileTransferRejected(FileRejectChatMessage message, DateTimeOffset timestampUtc)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_conversationLookup.TryGetValue(message.ConversationId, out var conversation))
            {
                conversation = Conversations.FirstOrDefault(c => c.DeviceId == message.ConversationId);
            }

            if (conversation is null)
            {
                return;
            }

            var offerItem = conversation.Messages
                .OfType<IncomingFileOfferChatMessageItem>()
                .FirstOrDefault(item => item.TransferId == message.TransferId);
            if (offerItem is not null)
            {
                offerItem.ReceiveProgress = 0d;
                offerItem.IsReceiving = false;
                offerItem.Text = $"[File] Receive failed {offerItem.FileName}";
                offerItem.IsHandled = true;
                offerItem.IsPending = false;
            }
            else
            {
                var outgoingItem = conversation.Messages
                    .FirstOrDefault(item => item.IsOutgoing && item.TrackingTransferId == message.TransferId);
                if (outgoingItem is not null)
                {
                    outgoingItem.ReceiveProgress = 0d;
                    outgoingItem.IsReceiving = false;
                    outgoingItem.IsPending = false;
                    outgoingItem.IsFailed = true;
                }
            }

            var timestamp = timestampUtc.ToLocalTime();
            conversation.SetLastMessage("[File] Receive failed", timestamp);
            SortConversations();
        });
    }

    private void OnFileTransferProgress(
        FileChatMessage message,
        long? bytesTransferred,
        long? totalBytes,
        DateTimeOffset timestampUtc)
    {
        if (_disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (!_conversationLookup.TryGetValue(message.ConversationId, out var conversation))
            {
                conversation = Conversations.FirstOrDefault(c => c.DeviceId == message.ConversationId);
            }

            if (conversation is null)
            {
                return;
            }

            var offerItem = conversation.Messages
                .OfType<IncomingFileOfferChatMessageItem>()
                .FirstOrDefault(item => item.TransferId == message.ChannelId);
            if (offerItem is not null)
            {
                var transferred = Math.Max(0L, bytesTransferred ?? 0L);
                var total = Math.Max(1L, totalBytes ?? offerItem.FileSizeBytes);
                var progress = Math.Clamp((double)transferred / total, 0d, 1d);
                offerItem.IsReceiving = true;
                offerItem.ReceiveProgress = progress;
                offerItem.Text = $"[File] Receiving {offerItem.FileName} ({progress * 100:0}%)";

                var timestamp = timestampUtc.ToLocalTime();
                conversation.SetLastMessage($"[File] {offerItem.FileName} ({progress * 100:0}%)", timestamp);
                return;
            }

            var fallbackIncoming = conversation.Messages
                .FirstOrDefault(item => item.IsIncoming && item.TrackingTransferId == message.ChannelId);
            if (fallbackIncoming is not null)
            {
                var transferredFallback = Math.Max(0L, bytesTransferred ?? 0L);
                var totalFallback = Math.Max(1L, totalBytes ?? fallbackIncoming.FileSizeBytes);
                var progressFallback = Math.Clamp((double)transferredFallback / totalFallback, 0d, 1d);
                fallbackIncoming.IsReceiving = true;
                fallbackIncoming.ReceiveProgress = progressFallback;
                fallbackIncoming.Text = $"[File] Receiving {fallbackIncoming.FileName} ({progressFallback * 100:0}%)";

                var timestampFallback = timestampUtc.ToLocalTime();
                conversation.SetLastMessage($"[File] {fallbackIncoming.FileName} ({progressFallback * 100:0}%)", timestampFallback);
                return;
            }

            var outgoingItem = conversation.Messages
                .FirstOrDefault(item => item.IsOutgoing && item.TrackingTransferId == message.ChannelId);
            if (outgoingItem is null)
            {
                return;
            }

            var transferredOut = Math.Max(0L, bytesTransferred ?? 0L);
            var totalOut = Math.Max(1L, totalBytes ?? outgoingItem.FileSizeBytes);
            var progressOut = Math.Clamp((double)transferredOut / totalOut, 0d, 1d);
            outgoingItem.IsReceiving = true;
            outgoingItem.ReceiveProgress = progressOut;
            outgoingItem.Text = $"[File] Sending {outgoingItem.FileName} ({progressOut * 100:0}%)";

            var timestampOut = timestampUtc.ToLocalTime();
            conversation.SetLastMessage($"[File] {outgoingItem.FileName} ({progressOut * 100:0}%)", timestampOut);
        });
    }

    private bool IsForegroundCurrentConversation(DeviceConversationItem conversation)
    {
        var mode = _messageAppService.ResolveIncomingDisplayMode(conversation.DeviceId);
        return mode == IncomingMessageDisplayMode.ShowInCurrentConversation;
    }

    private void SyncDisplayContext()
    {
        if (_disposed)
        {
            return;
        }

        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        var isMainWindowActive = mainWindow is not null && mainWindow.IsVisible && mainWindow.IsActive;
        var isDeviceChatPageOpen = string.Equals((mainWindow?.DataContext as MainWindowViewModel)?.Content as string,
            "DeviceChat",
            StringComparison.Ordinal);

        _messageAppService.UpdateDisplayContext(isMainWindowActive, isDeviceChatPageOpen, SelectedConversation?.DeviceId);
    }

    private static async Task<string?> PickSavePathAsync(string fileName)
    {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        if (mainWindow?.StorageProvider is null)
        {
            return null;
        }

        var extension = Path.GetExtension(fileName);
        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Save incoming file: {fileName}",
            SuggestedFileName = fileName,
            DefaultExtension = string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.')
        });

        return file?.Path.LocalPath;
    }

    private static int ResolvePort(DeviceConversationItem conversation, LocalDataTransportProtocol protocol,
        int offeredPort)
    {
        if (offeredPort > 0)
        {
            return offeredPort;
        }

        return protocol == LocalDataTransportProtocol.Quic ? conversation.QuicPort : conversation.TcpPort;
    }

    private async Task SendOfferDecisionWithFallbackAsync(
        DeviceConversationItem conversation,
        IncomingFileOfferChatMessageItem offer,
        Func<MessageContext, Task> action)
    {
        var primaryProtocol = offer.Protocol;
        var primaryPort = ResolvePort(conversation, primaryProtocol, offer.Port);
        if (primaryPort <= 0)
        {
            throw new InvalidOperationException("Invalid remote port for transfer decision.");
        }

        try
        {
            await action(BuildContext(conversation, primaryProtocol, primaryPort));
        }
        catch (Exception ex) when (primaryProtocol == LocalDataTransportProtocol.Quic && conversation.TcpPort > 0)
        {
            Logger.Warning(ex, "Send transfer decision over QUIC failed, fallback to TCP. DeviceId={DeviceId}",
                conversation.DeviceId);
            await action(BuildContext(conversation, LocalDataTransportProtocol.Tcp, conversation.TcpPort));
        }
    }

    private DeviceConversationItem? FindConversationByAddress(IPAddress remoteAddress)
    {
        var normalized = NormalizeAddress(remoteAddress);
        return Conversations.FirstOrDefault(conversation =>
            NormalizeAddress(conversation.Address).Equals(normalized));
    }

    private void SyncConversationsFromDiscovery()
    {
        var discovered = _deviceDiscoveryService.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList();

        var discoveredIds = new HashSet<string>(discovered.Select(device => device.Id), StringComparer.Ordinal);

        foreach (var (deviceId, trackedDevice) in _trackedDevices.Where(pair => !discoveredIds.Contains(pair.Key)).ToList())
        {
            trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
            _trackedDevices.Remove(deviceId);
        }

        foreach (var device in discovered)
        {
            if (!_trackedDevices.TryAdd(device.Id, device))
            {
                if (!ReferenceEquals(_trackedDevices[device.Id], device))
                {
                    _trackedDevices[device.Id].PropertyChanged -= OnTrackedDevicePropertyChanged;
                    _trackedDevices[device.Id] = device;
                }
            }

            device.PropertyChanged -= OnTrackedDevicePropertyChanged;
            device.PropertyChanged += OnTrackedDevicePropertyChanged;
            UpsertConversation(device);
        }

        foreach (var conversation in Conversations)
        {
            conversation.IsOnline = discoveredIds.Contains(conversation.DeviceId);
        }

        if (SelectedConversation is null && Conversations.Count > 0)
        {
            SelectedConversation = Conversations[0];
        }

        OnPropertyChanged(nameof(CurrentConversationTitle));
        OnPropertyChanged(nameof(CurrentConversationSubtitle));
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(HasNoConversations));
        SortConversations();
    }

    private void UpsertConversation(DeviceModel device)
    {
        if (!_conversationLookup.TryGetValue(device.Id, out var conversation))
        {
            conversation = new DeviceConversationItem(device.Id);
            _conversationLookup[device.Id] = conversation;
            Conversations.Add(conversation);
        }

        conversation.ApplyDevice(device);
    }

    private void SortConversations()
    {
        var sorted = Conversations
            .OrderByDescending(conversation => conversation.LastMessageAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(conversation => conversation.IsOnline)
            .ThenBy(conversation => conversation.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var index = 0; index < sorted.Count; index++)
        {
            var target = sorted[index];
            var current = Conversations.IndexOf(target);
            if (current >= 0 && current != index)
            {
                Conversations.Move(current, index);
            }
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    partial void OnSelectedConversationChanged(DeviceConversationItem? value)
    {
        if (value is not null)
        {
            value.UnreadCount = 0;
        }

        SyncDisplayContext();

        OnPropertyChanged(nameof(HasConversationSelected));
        OnPropertyChanged(nameof(ShowConversationPlaceholder));
        OnPropertyChanged(nameof(CurrentMessages));
        OnPropertyChanged(nameof(CurrentConversationTitle));
        OnPropertyChanged(nameof(CurrentConversationSubtitle));
        SendMessageCommand.NotifyCanExecuteChanged();
        PasteSendCommand.NotifyCanExecuteChanged();
    }

    partial void OnMessageTextChanged(string value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSendingChanged(bool value)
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        PasteSendCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _receiveCancellation.Cancel();
        _displayContextSyncTimer.Stop();
        _deviceDiscoveryService.Devices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
        foreach (var trackedDevice in _trackedDevices.Values)
        {
            trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
        }

        _trackedDevices.Clear();
        _receiveCancellation.Dispose();
    }
}

public partial class DeviceConversationItem : ObservableObject
{
    public DeviceConversationItem(string deviceId)
    {
        DeviceId = deviceId;
    }

    public string DeviceId { get; }
    public ObservableCollection<DeviceChatMessageItem> Messages { get; } = [];

    [ObservableProperty]
    private string _displayName = "Unknown Device";

    [ObservableProperty]
    private IPAddress _address = IPAddress.None;

    [ObservableProperty]
    private int _tcpPort;

    [ObservableProperty]
    private int _quicPort;

    [ObservableProperty]
    private bool _supportQuic;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    private string _lastMessagePreview = "No messages";

    [ObservableProperty]
    private DateTimeOffset? _lastMessageAt;

    [ObservableProperty]
    private int _unreadCount;

    public string AddressText => Address == IPAddress.None ? "Unknown Address" : Address.ToString();
    public string StatusText => IsOnline ? "Online" : "Offline";
    public bool HasUnread => UnreadCount > 0;
    public string UnreadCountText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();
    public string LastMessageTimeText => LastMessageAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty;

    public void ApplyDevice(DeviceModel device)
    {
        DisplayName = device.DisplayName;
        Address = device.Address;
        TcpPort = device.TcpPort;
        QuicPort = device.QuicPort;
        SupportQuic = device.SupportQuic;
        IsOnline = true;
    }

    public void SetLastMessage(string message, DateTimeOffset messageTime)
    {
        LastMessagePreview = BuildPreview(message);
        LastMessageAt = messageTime;
    }

    partial void OnAddressChanged(IPAddress value)
    {
        OnPropertyChanged(nameof(AddressText));
    }

    partial void OnIsOnlineChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnUnreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadCountText));
    }

    partial void OnLastMessageAtChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(LastMessageTimeText));
    }

    private static string BuildPreview(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ").Trim();
        if (singleLine.Length <= 36)
        {
            return singleLine;
        }

        return $"{singleLine[..36]}...";
    }
}

public partial class DeviceChatMessageItem : ObservableObject
{
    public DeviceChatMessageItem(string text, bool isOutgoing, DateTimeOffset timestamp)
    {
        _text = text;
        _isOutgoing = isOutgoing;
        _timestamp = timestamp;
    }

    public static DeviceChatMessageItem CreateImage(byte[]? imageBytes, bool isOutgoing, DateTimeOffset timestamp)
    {
        var item = new DeviceChatMessageItem("[Image]", isOutgoing, timestamp);
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return item;
        }

        try
        {
            using var stream = new MemoryStream(imageBytes, writable: false);
            item.ImagePreview = new Bitmap(stream);
        }
        catch
        {
            item.ImagePreview = null;
        }

        return item;
    }

    public static DeviceChatMessageItem CreateFile(string fileName, long sizeBytes, bool isOutgoing,
        DateTimeOffset timestamp)
    {
        var sizeKb = Math.Max(1, sizeBytes / 1024);
        return new DeviceChatMessageItem($"[File] {fileName} ({sizeKb} KB)", isOutgoing, timestamp)
        {
            FileName = fileName,
            FileSizeBytes = sizeBytes
        };
    }

    [ObservableProperty]
    private string _text;

    [ObservableProperty]
    private bool _isOutgoing;

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    [ObservableProperty]
    private bool _isPending;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private Bitmap? _imagePreview;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private long _fileSizeBytes;

    [ObservableProperty]
    private Guid? _trackingTransferId;

    [ObservableProperty]
    private double _receiveProgress;

    [ObservableProperty]
    private bool _isReceiving;

    public bool IsIncoming => !IsOutgoing;
    public bool HasImage => ImagePreview is not null;
    public bool HasFile => !string.IsNullOrWhiteSpace(FileName);
    public bool IsIncomingFileOffer => this is IncomingFileOfferChatMessageItem;
    public bool CanHandleIncomingOffer => this is IncomingFileOfferChatMessageItem offer && offer.CanHandle;
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");
    public string StateText => IsFailed ? "Failed" : IsReceiving ? $"{ReceiveProgress * 100:0}%" : IsPending ? "Sending..." : string.Empty;
    public bool HasState => !string.IsNullOrEmpty(StateText);

    partial void OnIsOutgoingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsIncoming));
    }

    partial void OnTimestampChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TimeText));
    }

    partial void OnIsPendingChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasText));
    }

    partial void OnImagePreviewChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(HasFile));
    }

    partial void OnReceiveProgressChanged(double value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnIsReceivingChanged(bool value)
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }
}

public partial class IncomingFileOfferChatMessageItem : DeviceChatMessageItem
{
    public IncomingFileOfferChatMessageItem(
        string conversationId,
        Guid transferId,
        string fileName,
        long sizeBytes,
        LocalDataTransportProtocol protocol,
        int port,
        DateTimeOffset timestamp)
        : base($"[File] {fileName} ({Math.Max(1, sizeBytes / 1024)} KB)", isOutgoing: false, timestamp)
    {
        _conversationId = conversationId;
        _transferId = transferId;
        FileName = fileName;
        FileSizeBytes = sizeBytes;
        _protocol = protocol;
        _port = port;
    }

    [ObservableProperty]
    private string _conversationId = string.Empty;

    [ObservableProperty]
    private Guid _transferId;

    [ObservableProperty]
    private LocalDataTransportProtocol _protocol;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private bool _isHandled;

    public bool CanHandle => !IsHandled;

    partial void OnIsHandledChanged(bool value)
    {
        OnPropertyChanged(nameof(CanHandle));
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
    }
}
