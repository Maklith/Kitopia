using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Input.Platform;
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
using Core.Services.Interfaces;
using Core.ViewModel.Main;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using Serilog;

namespace Core.ViewModel.Pages.device;

public partial class DeviceCommunicationPageViewModel : ObservableObject, IDisposable {
    private static readonly ILogger Logger = LogManager.Logger.ForContext<DeviceCommunicationPageViewModel>();
    private readonly IDeviceDiscoveryService _deviceDiscoveryService;
    private readonly IMessageAppService _messageAppService;
    private readonly IClipboardService _clipboardService;
    private readonly IToastService _toastService;
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly Task _receiveTask;
    private readonly DispatcherTimer _displayContextSyncTimer;
    private readonly DispatcherTimer _messageListAutoScrollTimer;
    private readonly Dictionary<string, DeviceConversationItem> _conversationLookup = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceModel> _trackedDevices = new(StringComparer.Ordinal);
    private readonly ObservableCollection<object> _emptyMessages = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _fileSendCancellations = new();
    private int _messageListVersion;
    private bool _disposed;

    public ObservableCollection<DeviceConversationItem> Conversations { get; } = [];

    public ObservableCollection<object> CurrentMessages =>
        SelectedConversation?.Messages ?? _emptyMessages;

    public string CurrentConversationTitle => SelectedConversation?.DisplayName ?? "设备聊天";

    public string CurrentConversationSubtitle => SelectedConversation is null
        ? "Select a device to start chatting"
        : $"{SelectedConversation.StatusText} - {SelectedConversation.AddressSummaryText}";

    public bool HasConversationSelected => SelectedConversation is not null;
    public bool ShowConversationPlaceholder => !HasConversationSelected;
    public bool HasConversations => Conversations.Count > 0;
    public bool HasNoConversations => !HasConversations;

    public int MessageListVersion {
        get => _messageListVersion;
        private set => SetProperty(ref _messageListVersion, value);
    }

    public DeviceCommunicationPageViewModel(
        IDeviceDiscoveryService deviceDiscoveryService,
        IMessageAppService messageAppService,
        IClipboardService clipboardService,
        IToastService toastService) {
        _deviceDiscoveryService = deviceDiscoveryService;
        _messageAppService = messageAppService;
        _clipboardService = clipboardService;
        _toastService = toastService;
        _displayContextSyncTimer = new DispatcherTimer {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _displayContextSyncTimer.Tick += (_, _) => SyncDisplayContext();
        _messageListAutoScrollTimer = new DispatcherTimer {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _messageListAutoScrollTimer.Tick += (_, _) => {
            _messageListAutoScrollTimer.Stop();
            MessageListVersion++;
        };

        _deviceDiscoveryService.Devices.CollectionChanged += OnDiscoveredDevicesCollectionChanged;
        _receiveTask = RunReceiveLoopAsync(_receiveCancellation.Token);
        SyncDisplayContext();
        _displayContextSyncTimer.Start();
        SyncConversationsFromDiscovery();
    }

    [ObservableProperty] private DeviceConversationItem? _selectedConversation;

    [ObservableProperty] private string _messageText = string.Empty;

    [ObservableProperty] private bool _isSending;

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync() {
        var conversation = SelectedConversation;
        var text = MessageText.Trim();
        if (conversation is null || string.IsNullOrWhiteSpace(text)) {
            return;
        }

        var message = new DeviceChatMessageItem(text, isOutgoing: true, DateTimeOffset.Now) {
            IsPending = true
        };

        conversation.Messages.Add(message);
        conversation.SetLastMessage(text, message.Timestamp);
        conversation.UnreadCount = 0;
        MessageText = string.Empty;
        SortConversations();
        RequestMessageListAutoScroll();

        IsSending = true;
        try {
            await SendToConversationAsync(conversation, text);
            message.IsPending = false;
            message.IsFailed = false;
        }
        catch (Exception ex) {
            message.IsPending = false;
            message.IsFailed = true;
            Logger.Warning(ex, "Send chat message failed. DeviceId={DeviceId}", conversation.DeviceId);
            _toastService.Show("设备聊天", $"消息发送失败: {ex.Message}", NotificationType.Error);
        }
        finally {
            IsSending = false;
        }
    }

    private bool CanSendMessage() {
        return !IsSending &&
               SelectedConversation is not null &&
               !string.IsNullOrWhiteSpace(MessageText);
    }

    private bool CanOperateConversation() {
        return !IsSending && SelectedConversation is not null;
    }

    private async Task SendToConversationAsync(DeviceConversationItem conversation, string text) {
        await _messageAppService.SendTextChatAsync(conversation.DeviceId, text);
    }

    private async Task SendImageToConversationAsync(DeviceConversationItem conversation, ImageChatMessage message,
        Stream stream) {
        await _messageAppService.SendImageChatAsync(conversation.DeviceId, message, stream);
    }

    private async Task SendFileToConversationAsync(DeviceConversationItem conversation, FileChatMessage message,
        Stream stream, CancellationToken cancellationToken = default) {
        await _messageAppService.SendFileChatAsync(conversation.DeviceId, message, stream, cancellationToken);
    }

    private async Task RunReceiveLoopAsync(CancellationToken token) {
        try {
            await foreach (var evt in _messageAppService.ReceiveAsync(token)) {
                switch (evt) {
                    case ChatMessageReceivedEvent { Message: TextChatMessage chatMessage }:
                        OnChatMessageReceived(chatMessage, evt.TimestampUtc);
                        break;
                    case ChatMessageReceivedEvent { Message: ImageChatMessage imageMessage } chatEvent:
                        OnImageMessageReceived(imageMessage, chatEvent.PayloadBytes, evt.TimestampUtc);
                        break;
                    case FileTransferUpdatedEvent { Status: FileTransferStatus.WaitingForAccept } transferEvent:
                        OnFileOfferReceived(transferEvent);
                        break;
                    case FileTransferUpdatedEvent { Status: FileTransferStatus.Accepted } transferEvent:
                        OnFileTransferAccepted(transferEvent);
                        break;
                    case FileTransferUpdatedEvent { Status: FileTransferStatus.InProgress } transferEvent:
                        OnFileTransferProgress(transferEvent);
                        break;
                    case FileTransferUpdatedEvent { Status: FileTransferStatus.Completed } transferEvent:
                        OnFileTransferCompleted(transferEvent);
                        break;
                    case FileTransferUpdatedEvent { Status: FileTransferStatus.Delivered } deliveredEvent:
                        OnFileTransferDelivered(deliveredEvent);
                        break;
                    case FileTransferUpdatedEvent transferEvent:
                        OnFileTransferRejected(transferEvent);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) {
            Logger.Warning(ex, "Message receive loop failed.");
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateConversation))]
    private async Task PasteSendAsync() {
        var conversation = SelectedConversation;
        if (conversation is null) {
            return;
        }

        var errors = new List<string>();

        if (_clipboardService.HasText()) {
            var text = _clipboardService.GetText()?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) {
                var textMessage = new DeviceChatMessageItem(text, isOutgoing: true, DateTimeOffset.Now) {
                    IsPending = true
                };
                conversation.Messages.Add(textMessage);
                conversation.SetLastMessage(text, textMessage.Timestamp);
                conversation.UnreadCount = 0;
                SortConversations();
                RequestMessageListAutoScroll();

                try {
                    await SendToConversationAsync(conversation, text);
                    textMessage.IsPending = false;
                    textMessage.IsFailed = false;
                }
                catch (Exception ex) {
                    textMessage.IsPending = false;
                    textMessage.IsFailed = true;
                    errors.Add($"text:{ex.Message}");
                }
            }
        }

        if (_clipboardService.HasImage()) {
            try {
                using var image = _clipboardService.GetImage();
                if (image is not null && !image.Empty()) {
                    Cv2.ImEncode(".png", image, out var imageBytes);
                    var imageBubble =
                        DeviceChatMessageItem.CreateImage(imageBytes, isOutgoing: true, DateTimeOffset.Now);
                    imageBubble.IsPending = true;
                    conversation.Messages.Add(imageBubble);
                    conversation.SetLastMessage("[图片]", imageBubble.Timestamp);
                    SortConversations();
                    RequestMessageListAutoScroll();

                    await using var imageStream = new MemoryStream(imageBytes, writable: false);
                    var imageMessage = new ImageChatMessage(conversation.DeviceId, Guid.NewGuid(),
                        imageBytes.LongLength,
                        "image/png", false);
                    try {
                        await SendImageToConversationAsync(conversation, imageMessage, imageStream);
                        imageBubble.IsPending = false;
                        imageBubble.IsFailed = false;
                    }
                    catch {
                        imageBubble.IsPending = false;
                        imageBubble.IsFailed = true;
                        throw;
                    }
                }
            }
            catch (Exception ex) {
                errors.Add($"image:{ex.Message}");
            }
        }

        if (_clipboardService.HasFiles()) {
            SendFilesAsync(conversation, _clipboardService.GetFiles(), errors);
        }

        if (errors.Count > 0) {
            _toastService.Show("设备聊天", $"监听和发送部分失败: {string.Join(";", errors)}",
                NotificationType.Warning);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOperateConversation))]
    private async Task SendFileAsync() {
        var conversation = SelectedConversation;
        if (conversation is null) {
            return;
        }

        var filePaths = await PickSendFilesAsync();
        if (filePaths.Count == 0) {
            return;
        }

        var errors = new List<string>();
        SendFilesAsync(conversation, filePaths, errors);
    }

    private void SendFilesAsync(DeviceConversationItem conversation, IReadOnlyCollection<string> filePaths,
        List<string> errors) {
        foreach (var filePath in filePaths) {
            if (!File.Exists(filePath)) continue;

            var fileInfo = new FileInfo(filePath);
            var appTool = ServiceManager.Services.GetService<IAppToolService>();
            var transferId = Guid.NewGuid();

            var fileMessage = new FileChatMessage(
                conversation.DeviceId,
                transferId,
                fileInfo.Name,
                fileInfo.Length);
            var fileBubble = new FileChatMessageItem(fileInfo.Name, fileInfo.Length, isOutgoing: true,
                DateTimeOffset.Now)
            {
                ConversationId = conversation.DeviceId,
                TrackingTransferId = transferId,
                LocalFilePath = filePath
            };
            conversation.Messages.Add(fileBubble);

            appTool?.LoadIcon(filePath, bmp =>
            {
                fileBubble.FileIcon = bmp;
            });

            conversation.SetLastMessage($"[文件] {fileInfo.Name}", fileBubble.Timestamp);

            var cts = new CancellationTokenSource();
            lock (_fileSendCancellations) { _fileSendCancellations[transferId] = cts; }

            var capturedPath = filePath;
            var capturedBubble = fileBubble;
            var capturedConversation = conversation;
            var capturedToken = cts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    var iconPng = appTool?.GetFileIconPng(capturedPath);
                    var msgWithIcon = new FileChatMessage(
                        capturedConversation.DeviceId,
                        transferId,
                        fileInfo.Name,
                        fileInfo.Length,
                        iconPng);
                    await using var fs = File.OpenRead(capturedPath);
                    await SendFileToConversationAsync(capturedConversation, msgWithIcon, fs, capturedToken);
                    ExecuteOnUiThread(() =>
                    {
                        capturedBubble.IsPending = false;
                        capturedBubble.IsReceiving = false;
                        capturedBubble.ReceiveProgress = 1d;
                        capturedBubble.IsFailed = false;
                    });
                }
                catch (Exception ex)
                {
                    if (capturedToken.IsCancellationRequested) return;
                    ExecuteOnUiThread(() =>
                    {
                        capturedBubble.IsPending = false;
                        capturedBubble.IsFailed = true;
                    });
                    lock (errors)
                    {
                        errors.Add($"file:{Path.GetFileName(capturedPath)}:{ex.Message}");
                    }
                }
                finally
                {
                    lock (_fileSendCancellations) { _fileSendCancellations.Remove(transferId); }
                    cts.Dispose();
                }
            });
        }

        SortConversations();
        RequestMessageListAutoScroll();
    }

    [RelayCommand]
    private async Task AcceptIncomingOfferAsync(FileChatMessageItem? messageItem) {
        if (messageItem is not { CanHandleIncomingOffer: true, TrackingTransferId: { } transferId } offer) {
            return;
        }

        var conversation = Conversations.FirstOrDefault(c => c.DeviceId == offer.ConversationId);
        if (conversation is null) {
            return;
        }

        try {
            var savePath = await PickSavePathAsync(offer.FileName);
            if (string.IsNullOrWhiteSpace(savePath) || !Path.IsPathRooted(savePath)) {
                return;
            }

            await _messageAppService.AcceptFileAsync(conversation.DeviceId, transferId, savePath);
            offer.IsHandled = true;
            offer.IsReceiving = true;
            offer.ReceiveProgress = 0d;
            offer.LocalFilePath = savePath;
            conversation.SetLastMessage($"[文件] {offer.FileName}", DateTimeOffset.Now);
            SortConversations();
        }
        catch (Exception ex) {
            _toastService.Show("设备聊天t", $"同意失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand]
    private async Task RejectIncomingOfferAsync(FileChatMessageItem? messageItem) {
        if (messageItem is not { CanHandleIncomingOffer: true, TrackingTransferId: { } transferId } offer) {
            return;
        }

        var conversation = Conversations.FirstOrDefault(c => c.DeviceId == offer.ConversationId);
        if (conversation is null) {
            return;
        }

        try {
            await _messageAppService.RejectFileAsync(conversation.DeviceId, transferId, "rejected_by_user");
            offer.IsHandled = true;
            conversation.SetLastMessage($"[文件] {offer.FileName}", DateTimeOffset.Now);
            SortConversations();
        }
        catch (Exception ex) {
            _toastService.Show("设备聊天", $"拒绝失败: {ex.Message}", NotificationType.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopyImage))]
    private async Task CopyImageAsync(DeviceChatMessageItem? messageItem) {
        if (messageItem?.ImageBytes is not { Length: > 0 } imageBytes) {
            return;
        }

        try {
            using var mat = Cv2.ImDecode(imageBytes, ImreadModes.Unchanged);
            if (mat.Empty()) {
                _toastService.Show("设备聊天", "图片复制失败: 图片数据无效", NotificationType.Warning);
                return;
            }

            var copied = await _clipboardService.SetImageAsync(new ScreenCaptureResult {
                Source = mat
            });
            _toastService.Show("设备聊天", copied ? "图片已复制到剪贴板" : "图片复制失败",
                copied ? NotificationType.Information : NotificationType.Warning);
        }
        catch (Exception ex) {
            Logger.Warning(ex, "Copy image to clipboard failed.");
            _toastService.Show("设备聊天", $"图片复制失败: {ex.Message}", NotificationType.Warning);
        }
    }

    private static bool CanCopyImage(DeviceChatMessageItem? messageItem) {
        return messageItem?.ImageBytes is { Length: > 0 };
    }

    [RelayCommand]
    private void OpenFile(FileChatMessageItem? item)
    {
        if (item?.HasLocalFile != true) return;
        try
        {
            Process.Start(new ProcessStartInfo(item.LocalFilePath!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "打开文件失败");
        }
    }

    [RelayCommand]
    private async Task SaveAsFileAsync(FileChatMessageItem? item)
    {
        if (item?.LocalFilePath is null) return;
        var topLevel = TopLevel.GetTopLevel(
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);
        if (topLevel is null) return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = item.FileName
        });
        if (file is null) return;
        await using var src = File.OpenRead(item.LocalFilePath);
        await using var dst = await file.OpenWriteAsync();
        await src.CopyToAsync(dst);
    }

    [RelayCommand]
    private async Task CopyFileAsync(FileChatMessageItem? item)
    {
        if (item?.LocalFilePath is null) return;
        try
        {
            var topLevel = TopLevel.GetTopLevel(
                Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null);
            if (topLevel?.Clipboard is null) return;
            await topLevel.Clipboard.SetTextAsync(item.LocalFilePath);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "复制文件失败");
        }
    }

    [RelayCommand]
    private void CancelTransfer(FileChatMessageItem? item)
    {
        if (item?.TrackingTransferId is null) return;

        var transferId = item.TrackingTransferId.Value;
        lock (_fileSendCancellations)
        {
            if (_fileSendCancellations.TryGetValue(transferId, out var cts))
            {
                cts.Cancel();
            }
        }

        var deviceId = SelectedConversation?.DeviceId ?? item.ConversationId;
        if (!string.IsNullOrWhiteSpace(deviceId))
            _ = _messageAppService.CancelTransferAsync(deviceId, transferId, "user_cancelled");
        item.IsFailed = true;
        item.IsReceiving = false;
        item.IsHandled = true;
    }

    [RelayCommand]
    private void ViewFileDetails(FileChatMessageItem? item)
    {
        if (item is null) return;
        var topLevel = TopLevel.GetTopLevel(
            Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);
        var details = $"文件名：{item.FileName}\n大小：{item.FileSizeText}\n路径：{item.LocalFilePath ?? "暂无"}\n状态：{item.StateText}";
        _toastService.Show(new ToastRequest
        {
            Header = "文件详情",
            Text = details,
            AutoCloseDelay = null
        }, topLevel as Avalonia.Controls.Window);
    }

    private void ExecuteOnUiThread(Action action) {
        if (_disposed) {
            return;
        }

        Dispatcher.UIThread.Post(() => {
            if (_disposed) {
                return;
            }

            action();
        });
    }

    private bool TryGetConversation(string conversationId, out DeviceConversationItem? conversation) {
        if (!_conversationLookup.TryGetValue(conversationId, out conversation)) {
            conversation = Conversations.FirstOrDefault(c => c.DeviceId == conversationId);
        }

        return conversation is not null;
    }

    private static FileChatMessageItem? FindOutgoingFileItem(DeviceConversationItem conversation, Guid transferId)
    {
        return conversation.Messages.OfType<FileChatMessageItem>()
            .FirstOrDefault(item => item.IsOutgoing && item.TrackingTransferId == transferId);
    }

    private static FileChatMessageItem? FindIncomingFileItem(DeviceConversationItem conversation, Guid transferId)
    {
        return conversation.Messages.OfType<FileChatMessageItem>()
            .FirstOrDefault(item => !item.IsOutgoing && item.IsIncomingFileOffer && item.TrackingTransferId == transferId);
    }

    private static FileChatMessageItem? FindFileItemByTransferId(DeviceConversationItem conversation, Guid transferId)
    {
        return conversation.Messages.OfType<FileChatMessageItem>()
            .FirstOrDefault(item => item.TrackingTransferId == transferId);
    }

    private void OnDiscoveredDevicesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) {
        if (_disposed) {
            return;
        }

        Dispatcher.UIThread.Post(SyncConversationsFromDiscovery);
    }

    private void OnTrackedDevicePropertyChanged(object? sender, PropertyChangedEventArgs e) {
        if (_disposed || sender is not DeviceModel device) {
            return;
        }

        ExecuteOnUiThread(() => {
            UpsertConversation(device);
            OnPropertyChanged(nameof(CurrentConversationTitle));
            OnPropertyChanged(nameof(CurrentConversationSubtitle));
            SortConversations();
        });
    }

    private void OnChatMessageReceived(TextChatMessage message, DateTimeOffset timestampUtc) {
        if (_disposed) {
            return;
        }

        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) {
                Logger.Debug(
                    "Drop chat message because sender device is not discovered. DeviceId={DeviceId}",
                    message.ConversationId);
                return;
            }

            var text = message.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) {
                return;
            }

            var timestamp = timestampUtc.ToLocalTime();
            conversation.Messages.Add(new DeviceChatMessageItem(text, isOutgoing: false, timestamp));
            conversation.SetLastMessage(text, timestamp);

            if (!IsForegroundCurrentConversation(conversation)) {
                conversation.UnreadCount++;
            }
            else {
                conversation.UnreadCount = 0;
            }

            SortConversations();
            RequestMessageListAutoScroll();
        });
    }

    private void OnImageMessageReceived(ImageChatMessage message, byte[]? payloadBytes, DateTimeOffset timestampUtc) {
        if (_disposed) {
            return;
        }

        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) {
                Logger.Debug("Drop image message because sender device is not discovered. DeviceId={DeviceId}",
                    message.ConversationId);
                return;
            }

            var timestamp = timestampUtc.ToLocalTime();
            var imageItem = DeviceChatMessageItem.CreateImage(payloadBytes, isOutgoing: false, timestamp);
            if (imageItem.ImagePreview is null) {
                imageItem.Text = $"[图片] {DeviceChatMessageItem.FormatFileSizeLabel(message.SizeBytes)}";
            }

            conversation.Messages.Add(imageItem);
            conversation.SetLastMessage("[图片]", timestamp);

            if (!IsForegroundCurrentConversation(conversation)) {
                conversation.UnreadCount++;
            }
            else {
                conversation.UnreadCount = 0;
            }

            SortConversations();
            RequestMessageListAutoScroll();
        });
    }

    private void OnFileOfferReceived(FileTransferUpdatedEvent message) {
        if (_disposed) return;

        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) return;

            var fileBubble = new FileChatMessageItem(
                message.FileName ?? "未知文件",
                message.TotalBytes ?? 0,
                isOutgoing: false,
                message.TimestampUtc.ToLocalTime())
            {
                ConversationId = message.ConversationId,
                TrackingTransferId = message.TransferId,
                IsIncomingFileOffer = true
            };

            if (message.IconPng is { Length: > 0 })
            {
                try
                {
                    using var ms = new MemoryStream(message.IconPng);
                    fileBubble.FileIcon = new Avalonia.Media.Imaging.Bitmap(ms);
                }
                catch { /* fall back to extension-based icon */ }
            }

            if (fileBubble.FileIcon is null)
            {
                var ext = !string.IsNullOrWhiteSpace(message.FileName)
                    ? Path.GetExtension(message.FileName)
                    : null;
                if (ext is not null)
                {
                    ServiceManager.Services.GetService<IAppToolService>()?.LoadIcon(
                        $"file{ext}", bmp => fileBubble.FileIcon = bmp);
                }
            }

            conversation.Messages.Add(fileBubble);
            conversation.SetLastMessage($"[文件] {message.FileName}", fileBubble.Timestamp);
            SortConversations();
            RequestMessageListAutoScroll();
        });
    }

    private void OnFileTransferCompleted(FileTransferUpdatedEvent message) {
        if (_disposed) return;
        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) return;

            var fileItem = FindFileItemByTransferId(conversation, message.TransferId);
            if (fileItem is not null)
            {
                fileItem.ReceiveProgress = 1d;
                fileItem.IsReceiving = false;
                fileItem.ResetTransferSpeed();
                fileItem.IsHandled = true;
                fileItem.IsPending = false;

                // Refresh icon from actual saved file
                if (!string.IsNullOrWhiteSpace(fileItem.LocalFilePath) && File.Exists(fileItem.LocalFilePath))
                {
                    ServiceManager.Services.GetService<IAppToolService>()?.LoadIcon(
                        fileItem.LocalFilePath, bmp => fileItem.FileIcon = bmp);
                }
            }

            conversation.SetLastMessage("[文件] 已完成", message.TimestampUtc.ToLocalTime());
            SortConversations();
            RequestMessageListAutoScroll();
        });
    }

    private void OnFileTransferRejected(FileTransferUpdatedEvent message) {
        if (_disposed) return;
        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) return;

            var fileItem = FindFileItemByTransferId(conversation, message.TransferId);
            if (fileItem is null) return;

            fileItem.ReceiveProgress = 0d;
            fileItem.IsReceiving = false;
            fileItem.ResetTransferSpeed();
            fileItem.IsPending = false;
            fileItem.IsFailed = true;
            fileItem.IsHandled = true;

            conversation.SetLastMessage("[文件] 传输失败", message.TimestampUtc.ToLocalTime());
            SortConversations();
            RequestMessageListAutoScroll();
        });
    }

    private void OnFileTransferProgress(
        FileChatMessage message,
        long? bytesTransferred,
        long? totalBytes,
        DateTimeOffset timestampUtc) {
        OnFileTransferProgress(new FileTransferUpdatedEvent(
            message.ConversationId,
            message.ChannelId,
            FileTransferDirection.Upload,
            FileTransferStatus.InProgress,
            message.FileName,
            bytesTransferred,
            totalBytes,
            null,
            timestampUtc));
    }

    private void OnFileTransferAccepted(FileTransferUpdatedEvent message) {
        if (_disposed) return;
        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) return;

            var outgoingItem = FindOutgoingFileItem(conversation, message.TransferId);
            if (outgoingItem is not null)
            {
                outgoingItem.IsWaitingForAccept = false;
                outgoingItem.IsPending = false;
                outgoingItem.IsFailed = false;
                outgoingItem.IsReceiving = true;
            }

            conversation.SetLastMessage("[文件] 对方已同意接收", message.TimestampUtc.ToLocalTime());
            SortConversations();
            RequestMessageListAutoScroll();
        });
    }

    private void OnFileTransferDelivered(FileTransferUpdatedEvent message) {
        if (_disposed) return;
        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) return;

            var outgoingItem = FindOutgoingFileItem(conversation, message.TransferId);
            if (outgoingItem is not null)
            {
                outgoingItem.IsOfferDelivered = true;
                outgoingItem.IsWaitingForAccept = true;
            }
        });
    }

    private void OnFileTransferProgress(FileTransferUpdatedEvent message) {
        if (_disposed) return;
        ExecuteOnUiThread(() => {
            if (!TryGetConversation(message.ConversationId, out var conversation)) return;

            var transferred = Math.Max(0L, message.BytesTransferred ?? 0L);
            var total = Math.Max(1L, message.TotalBytes ?? 0);
            var progress = Math.Clamp((double)transferred / total, 0d, 1d);

            var fileItem = FindFileItemByTransferId(conversation, message.TransferId);
            if (fileItem is null) return;

            // Throttle UI refresh to avoid flickering
            if (!fileItem.CanUpdateProgress(message.TimestampUtc)) return;

            fileItem.IsReceiving = true;
            fileItem.ReceiveProgress = progress;
            fileItem.UpdateTransferSpeed(transferred, message.TimestampUtc);
            conversation.SetLastMessage($"[文件] {fileItem.FileName} ({progress * 100:0.0}%)", message.TimestampUtc.ToLocalTime());
            RequestMessageListAutoScroll();
        });
    }

    private void ShowPersistentFileSendErrorToast(string text) {
        _toastService.Show(new ToastRequest {
            Header = "设备聊天",
            Text = text,
            NotificationType = NotificationType.Error,
            AutoCloseDelay = null
        });
    }

    private bool IsForegroundCurrentConversation(DeviceConversationItem conversation) {
        var mode = _messageAppService.ResolveIncomingDisplayMode(conversation.DeviceId);
        return mode == IncomingMessageDisplayMode.ShowInCurrentConversation;
    }

    private void RequestMessageListAutoScroll() {
        if (_disposed) {
            return;
        }

        _messageListAutoScrollTimer.Stop();
        _messageListAutoScrollTimer.Start();
    }

    private void SyncDisplayContext() {
        if (_disposed) {
            return;
        }

        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        var isMainWindowActive = mainWindow is not null && mainWindow.IsVisible && mainWindow.IsActive;
        var isDeviceChatPageOpen = string.Equals((mainWindow?.DataContext as MainWindowViewModel)?.Content as string,
            "DeviceChat",
            StringComparison.Ordinal);

        _messageAppService.UpdateDisplayContext(isMainWindowActive, isDeviceChatPageOpen,
            SelectedConversation?.DeviceId);
    }

    private static async Task<string?> PickSavePathAsync(string fileName) {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        if (mainWindow?.StorageProvider is null) {
            return null;
        }

        var extension = Path.GetExtension(fileName);
        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            Title = $"Save incoming file: {fileName}",
            SuggestedFileName = fileName,
            DefaultExtension = string.IsNullOrWhiteSpace(extension) ? null : extension.TrimStart('.')
        });

        return file?.Path.LocalPath;
    }

    private static async Task<IReadOnlyList<string>> PickSendFilesAsync() {
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = desktop?.MainWindow;
        if (mainWindow?.StorageProvider is null) {
            return [];
        }

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            Title = "选择要发送的文件",
            AllowMultiple = true
        });

        return files
            .Select(file => file.Path.LocalPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
    }

    private DeviceConversationItem? FindConversationByAddress(IPAddress remoteAddress) {
        var normalized = NormalizeAddress(remoteAddress);
        return Conversations.FirstOrDefault(conversation =>
            NormalizeAddress(conversation.Ipv4Address).Equals(normalized) ||
            NormalizeAddress(conversation.Ipv6Address).Equals(normalized));
    }

    private void SyncConversationsFromDiscovery() {
        var discovered = _deviceDiscoveryService.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .ToList();

        var discoveredIds = new HashSet<string>(discovered.Select(device => device.Id), StringComparer.Ordinal);

        foreach (var (deviceId, trackedDevice) in _trackedDevices.Where(pair => !discoveredIds.Contains(pair.Key))
                     .ToList()) {
            trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
            _trackedDevices.Remove(deviceId);
        }

        foreach (var device in discovered) {
            if (!_trackedDevices.TryAdd(device.Id, device)) {
                if (!ReferenceEquals(_trackedDevices[device.Id], device)) {
                    _trackedDevices[device.Id].PropertyChanged -= OnTrackedDevicePropertyChanged;
                    _trackedDevices[device.Id] = device;
                }
            }

            device.PropertyChanged -= OnTrackedDevicePropertyChanged;
            device.PropertyChanged += OnTrackedDevicePropertyChanged;
            UpsertConversation(device);
        }

        foreach (var conversation in Conversations) {
            conversation.IsOnline = discoveredIds.Contains(conversation.DeviceId);
        }

        if (SelectedConversation is null && Conversations.Count > 0) {
            SelectedConversation = Conversations[0];
        }

        var requestedConversationId = _messageAppService.GetRequestedConversationId();
        if (!string.IsNullOrWhiteSpace(requestedConversationId)) {
            var requestedConversation = Conversations.FirstOrDefault(item =>
                string.Equals(item.DeviceId, requestedConversationId, StringComparison.Ordinal));
            if (requestedConversation is not null) {
                SelectedConversation = requestedConversation;
                _messageAppService.ClearRequestedConversationId();
            }
        }

        OnPropertyChanged(nameof(CurrentConversationTitle));
        OnPropertyChanged(nameof(CurrentConversationSubtitle));
        OnPropertyChanged(nameof(HasConversations));
        OnPropertyChanged(nameof(HasNoConversations));
        SortConversations();
    }

    private void UpsertConversation(DeviceModel device) {
        if (!_conversationLookup.TryGetValue(device.Id, out var conversation)) {
            conversation = new DeviceConversationItem(device.Id);
            _conversationLookup[device.Id] = conversation;
            Conversations.Add(conversation);
        }

        conversation.ApplyDevice(device);
    }

    private void SortConversations() {
        var selectedConversation = SelectedConversation;
        var sorted = Conversations
            .OrderByDescending(conversation => conversation.LastMessageAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(conversation => conversation.IsOnline)
            .ThenBy(conversation => conversation.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        for (var index = 0; index < sorted.Count; index++) {
            var target = sorted[index];
            var current = Conversations.IndexOf(target);
            if (current >= 0 && current != index) {
                Conversations.Move(current, index);
            }
        }

        if (selectedConversation is not null &&
            SelectedConversation is null &&
            Conversations.Contains(selectedConversation)) {
            SelectedConversation = selectedConversation;
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address) {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    partial void OnSelectedConversationChanged(DeviceConversationItem? value) {
        if (value is not null) {
            value.UnreadCount = 0;
        }

        SyncDisplayContext();

        OnPropertyChanged(nameof(HasConversationSelected));
        OnPropertyChanged(nameof(ShowConversationPlaceholder));
        OnPropertyChanged(nameof(CurrentMessages));
        OnPropertyChanged(nameof(CurrentConversationTitle));
        OnPropertyChanged(nameof(CurrentConversationSubtitle));
        RequestMessageListAutoScroll();
        SendMessageCommand.NotifyCanExecuteChanged();
        PasteSendCommand.NotifyCanExecuteChanged();
        SendFileCommand.NotifyCanExecuteChanged();
    }

    partial void OnMessageTextChanged(string value) {
        SendMessageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSendingChanged(bool value) {
        SendMessageCommand.NotifyCanExecuteChanged();
        PasteSendCommand.NotifyCanExecuteChanged();
        SendFileCommand.NotifyCanExecuteChanged();
    }

    public void Dispose() {
        if (_disposed) {
            return;
        }

        _disposed = true;
        _receiveCancellation.Cancel();
        _displayContextSyncTimer.Stop();
        _messageListAutoScrollTimer.Stop();
        _deviceDiscoveryService.Devices.CollectionChanged -= OnDiscoveredDevicesCollectionChanged;
        foreach (var trackedDevice in _trackedDevices.Values) {
            trackedDevice.PropertyChanged -= OnTrackedDevicePropertyChanged;
        }

        _trackedDevices.Clear();

        lock (_fileSendCancellations)
        {
            foreach (var cts in _fileSendCancellations.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _fileSendCancellations.Clear();
        }

        _receiveCancellation.Dispose();
    }
}

public partial class DeviceConversationItem : ObservableObject {
    public DeviceConversationItem(string deviceId) {
        DeviceId = deviceId;
    }

    public string DeviceId { get; }
    public ObservableCollection<object> Messages { get; } = [];

    [ObservableProperty] private string _displayName = "Unknown Device";

    [ObservableProperty] private IPAddress _ipv4Address = IPAddress.None;

    [ObservableProperty] private IPAddress _ipv6Address = IPAddress.None;

    [ObservableProperty] private int _tcpPort;

    [ObservableProperty] private bool _isOnline;

    [ObservableProperty] private string _lastMessagePreview = "无消息";

    [ObservableProperty] private DateTimeOffset? _lastMessageAt;

    [ObservableProperty] private int _unreadCount;

    public bool HasIpv4 => Ipv4Address != IPAddress.None;
    public bool HasIpv6 => Ipv6Address != IPAddress.None;
    public string Ipv4AddressText => HasIpv4 ? Ipv4Address.ToString() : "Unknown IPv4";
    public string Ipv6AddressText => Ipv6Address.ToString();

    public string AddressSummaryText {
        get {
            if (HasIpv4 && HasIpv6) {
                return $"IPv4: {Ipv4AddressText} | IPv6: {Ipv6AddressText}";
            }

            if (HasIpv4)
                return $"IPv4: {Ipv4AddressText}";
            if (HasIpv6) {
                return $"IPv6: {Ipv6AddressText}";
            }

            return "未知IP";
        }
    }

    public IPAddress PreferredTransportAddress => Ipv6Address != IPAddress.None ? Ipv6Address : Ipv4Address;
        public string StatusText => IsOnline ? "在线" : "离线";
    public bool HasUnread => UnreadCount > 0;
    public string UnreadCountText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();
    public string LastMessageTimeText => LastMessageAt?.ToLocalTime().ToString("HH:mm") ?? string.Empty;

    public void ApplyDevice(DeviceModel device) {
        DisplayName = device.DisplayName;
        Ipv4Address = device.Ipv4Address;
        Ipv6Address = device.Ipv6Address;
        TcpPort = device.TcpPort;
        IsOnline = true;
    }

    public void SetLastMessage(string message, DateTimeOffset messageTime) {
        LastMessagePreview = BuildPreview(message);
        LastMessageAt = messageTime;
    }

    partial void OnIpv4AddressChanged(IPAddress value) {
        OnPropertyChanged(nameof(HasIpv4));
        OnPropertyChanged(nameof(Ipv4AddressText));
        OnPropertyChanged(nameof(AddressSummaryText));
        OnPropertyChanged(nameof(PreferredTransportAddress));
    }

    partial void OnIpv6AddressChanged(IPAddress value) {
        OnPropertyChanged(nameof(HasIpv6));
        OnPropertyChanged(nameof(Ipv6AddressText));
        OnPropertyChanged(nameof(AddressSummaryText));
        OnPropertyChanged(nameof(PreferredTransportAddress));
    }

    partial void OnIsOnlineChanged(bool value) {
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnUnreadCountChanged(int value) {
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadCountText));
    }

    partial void OnLastMessageAtChanged(DateTimeOffset? value) {
        OnPropertyChanged(nameof(LastMessageTimeText));
    }

    private static string BuildPreview(string message) {
        var singleLine = message.ReplaceLineEndings(" ").Trim();
        if (singleLine.Length <= 36) {
            return singleLine;
        }

        return $"{singleLine[..36]}...";
    }
}

public partial class DeviceChatMessageItem : ObservableObject {
    public DeviceChatMessageItem(string text, bool isOutgoing, DateTimeOffset timestamp) {
        _text = text;
        _isOutgoing = isOutgoing;
        _timestamp = timestamp;
    }

    public static DeviceChatMessageItem CreateImage(byte[]? imageBytes, bool isOutgoing, DateTimeOffset timestamp) {
        var item = new DeviceChatMessageItem(string.Empty, isOutgoing, timestamp);
        if (imageBytes is null || imageBytes.Length == 0) {
            return item;
        }

        item.ImageBytes = imageBytes.ToArray();

        try {
            using var stream = new MemoryStream(imageBytes, writable: false);
            item.ImagePreview = new Bitmap(stream);
        }
        catch {
            item.ImagePreview = null;
        }

        return item;
    }

    public static DeviceChatMessageItem CreateFile(string fileName, long sizeBytes, bool isOutgoing,
        DateTimeOffset timestamp) {
        return new DeviceChatMessageItem($"[文件] {fileName} ({FormatFileSizeLabel(sizeBytes)})", isOutgoing, timestamp) {
            FileName = fileName,
            FileSizeBytes = sizeBytes
        };
    }

    public static DeviceChatMessageItem CreateIncomingFileOffer(
        string conversationId,
        Guid transferId,
        string fileName,
        long sizeBytes,
        DateTimeOffset timestamp) {
        return new DeviceChatMessageItem($"[文件] {fileName} ({FormatFileSizeLabel(sizeBytes)})", isOutgoing: false, timestamp) {
            ConversationId = conversationId,
            FileName = fileName,
            FileSizeBytes = sizeBytes,
            TrackingTransferId = transferId,
            IsIncomingFileOffer = true
        };
    }

    public static string FormatFileSizeLabel(long sizeBytes) {
        var bytes = Math.Max(0L, sizeBytes);
        const long oneKb = 1024;
        const long oneMb = 1024L * 1024L;
        const long oneGb = 1024L * 1024L * 1024L;

        if (bytes >= oneGb) {
            return $"{bytes / (double)oneGb:0.00} GB";
        }

        if (bytes >= oneMb) {
            return $"{bytes / (double)oneMb:0.00} MB";
        }

        if (bytes >= oneKb) {
            return $"{bytes / (double)oneKb:0.00} KB";
        }

        return $"{bytes} 字节";
    }

    [ObservableProperty] private string _text;

    [ObservableProperty] private bool _isOutgoing;

    [ObservableProperty] private DateTimeOffset _timestamp;

    [ObservableProperty] private bool _isPending;

    [ObservableProperty] private bool _isFailed;

    [ObservableProperty] private Bitmap? _imagePreview;

    [ObservableProperty] private byte[]? _imageBytes;

    [ObservableProperty] private string _fileName = string.Empty;

    [ObservableProperty] private long _fileSizeBytes;

    [ObservableProperty] private Guid? _trackingTransferId;

    [ObservableProperty] private double _receiveProgress;

    [ObservableProperty] private bool _isReceiving;

    [ObservableProperty] private double _transferSpeedBytesPerSecond;

    [ObservableProperty] private string _conversationId = string.Empty;

    [ObservableProperty] private bool _isIncomingFileOffer;

    [ObservableProperty] private bool _isHandled;

    private long _transferStartBytes = -1;
    private DateTimeOffset? _transferStartTimestampUtc;

    public bool IsIncoming => !IsOutgoing;
    public bool HasImage => ImagePreview is not null;
    public bool HasFile => !string.IsNullOrWhiteSpace(FileName);
    public bool CanHandleIncomingOffer => IsIncomingFileOffer && !IsHandled && TrackingTransferId.HasValue;
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
    public string TimeText => Timestamp.ToLocalTime().ToString("HH:mm");

    public string StateText => IsFailed
        ? "失败"
        : IsReceiving
            ? BuildTransferStateText()
            : IsPending
                ? "发送中..."
                : string.Empty;

    public bool HasState => !string.IsNullOrEmpty(StateText);

    partial void OnIsOutgoingChanged(bool value) {
        OnPropertyChanged(nameof(IsIncoming));
    }

    partial void OnTimestampChanged(DateTimeOffset value) {
        OnPropertyChanged(nameof(TimeText));
    }

    partial void OnIsPendingChanged(bool value) {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnIsFailedChanged(bool value) {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnTextChanged(string value) {
        OnPropertyChanged(nameof(HasText));
    }

    partial void OnImagePreviewChanged(Bitmap? value) {
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnFileNameChanged(string value) {
        OnPropertyChanged(nameof(HasFile));
    }

    partial void OnTrackingTransferIdChanged(Guid? value) {
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
    }

    partial void OnIsIncomingFileOfferChanged(bool value) {
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
    }

    partial void OnIsHandledChanged(bool value) {
        OnPropertyChanged(nameof(CanHandleIncomingOffer));
    }

    partial void OnReceiveProgressChanged(double value) {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnIsReceivingChanged(bool value) {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    partial void OnTransferSpeedBytesPerSecondChanged(double value) {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(HasState));
    }

    public void UpdateTransferSpeed(long transferredBytes, DateTimeOffset timestampUtc) {
        if (!_transferStartTimestampUtc.HasValue || transferredBytes < _transferStartBytes) {
            _transferStartBytes = Math.Max(0L, transferredBytes);
            _transferStartTimestampUtc = timestampUtc;
            TransferSpeedBytesPerSecond = 0d;
            return;
        }

        var elapsedSeconds = (timestampUtc - _transferStartTimestampUtc.Value).TotalSeconds;
        if (elapsedSeconds <= 0.0001d) {
            return;
        }

        var elapsedBytes = Math.Max(0L, transferredBytes - _transferStartBytes);
        TransferSpeedBytesPerSecond = Math.Max(0d, elapsedBytes / elapsedSeconds);
    }

    public void ResetTransferSpeed() {
        _transferStartBytes = -1;
        _transferStartTimestampUtc = null;
        TransferSpeedBytesPerSecond = 0d;
    }

    private string BuildTransferStateText() {
        var progressText = $"{ReceiveProgress * 100:0.0}%";
        if (TransferSpeedBytesPerSecond <= 0d) {
            return progressText;
        }

        return $"{progressText} | {FormatBytes(TransferSpeedBytesPerSecond)}/s";
    }

    private static string FormatBytes(double bytes) {
        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = Math.Max(0d, bytes);
        var unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1) {
            value /= 1024d;
            unitIndex++;
        }

        if (value >= 100d) {
            return $"{value:0} {units[unitIndex]}";
        }

        if (value >= 10d) {
            return $"{value:0.0} {units[unitIndex]}";
        }

        return $"{value:0.00} {units[unitIndex]}";
    }
}
