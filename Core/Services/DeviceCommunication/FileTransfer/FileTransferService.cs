using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Core.Services.DeviceCommunication.Presentation;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Requests;
using Core.Services.DeviceCommunication.Transport;
using PluginCore;

namespace Core.Services.DeviceCommunication.FileTransfer;

public sealed partial class FileTransferService : IFileTransferService
{
    private static readonly HashSet<string> ImageFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif"
    };

    private static readonly TimeSpan TransferToastUpdateInterval = TimeSpan.FromMilliseconds(200);

    private readonly IRequestTracker _requestTracker;
    private readonly ITransportService _transportService;
    private readonly IFilePickerService _filePickerService;
    private readonly IClipboardAssetExtractor _clipboardAssetExtractor;
    private readonly IChatHistoryService _chatHistoryService;
    private readonly INotificationService _notificationService;
    private readonly Func<int> _getAdvertisedPort;
    private readonly Func<Guid> _getLocalDeviceId;
    private readonly Func<string> _getLocalDeviceName;
    private readonly Func<DeviceModel, bool> _shouldShowExternalToastForSender;

    private readonly ConcurrentDictionary<string, string> _pendingDownloads = new();
    private readonly ConcurrentDictionary<string, IncomingFileRequestContext> _pendingIncomingFileRequests = new();
    private readonly ConcurrentDictionary<string, IToastProgressHandle> _sendingTransferToasts = new();
    private readonly ConcurrentDictionary<string, IToastProgressHandle> _receivingTransferToasts = new();

    public FileTransferService(
        IRequestTracker requestTracker,
        ITransportService transportService,
        IFilePickerService filePickerService,
        IClipboardAssetExtractor clipboardAssetExtractor,
        IChatHistoryService chatHistoryService,
        INotificationService notificationService,
        Func<int> getAdvertisedPort,
        Func<Guid> getLocalDeviceId,
        Func<string> getLocalDeviceName,
        Func<DeviceModel, bool> shouldShowExternalToastForSender)
    {
        _requestTracker = requestTracker;
        _transportService = transportService;
        _filePickerService = filePickerService;
        _clipboardAssetExtractor = clipboardAssetExtractor;
        _chatHistoryService = chatHistoryService;
        _notificationService = notificationService;
        _getAdvertisedPort = getAdvertisedPort;
        _getLocalDeviceId = getLocalDeviceId;
        _getLocalDeviceName = getLocalDeviceName;
        _shouldShowExternalToastForSender = shouldShowExternalToastForSender;
    }

    public event EventHandler<FileTransferRequestEventArgs>? FileRequestReceived;
    public event EventHandler<FileTransferProgressEventArgs>? TransferProgress;
    public event EventHandler<FileTransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferInterruptionEventArgs>? TransferInterrupted;
    public event EventHandler<DeviceStreamReceivedEventArgs>? StreamReceived;

    public async Task RequestFileTransferAsync(DeviceModel target, string? filePath = null)
    {
        filePath = string.IsNullOrWhiteSpace(filePath) ? await _filePickerService.PickFileToSendAsync() : filePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        await RequestFileTransferInternalAsync(
            target,
            filePath,
            transferKindLabel: IsImageFilePath(filePath) ? "图片" : "文件");
    }

    public async Task RequestImageTransferAsync(DeviceModel target, string? imagePath = null)
    {
        imagePath = string.IsNullOrWhiteSpace(imagePath) ? await _filePickerService.PickImageToSendAsync() : imagePath;
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        await RequestFileTransferInternalAsync(target, imagePath, transferKindLabel: "图片");
    }

    public async Task<int> RequestClipboardTransferAsync(DeviceModel target)
    {
        var clipboardPaths = (await _clipboardAssetExtractor.TryGetClipboardFilePathsAsync()).ToList();
        var clipboardImagePath = _clipboardAssetExtractor.TryExtractClipboardImageToTempFilePath();
        if (!string.IsNullOrWhiteSpace(clipboardImagePath))
        {
            clipboardPaths.Add(clipboardImagePath);
        }

        var filesToSend = clipboardPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (filesToSend.Count == 0)
        {
            throw new InvalidOperationException("剪贴板中没有可发送的文件或图片。");
        }

        var sentCount = 0;
        var failed = new List<string>();
        foreach (var path in filesToSend)
        {
            try
            {
                await RequestFileTransferInternalAsync(
                    target,
                    path,
                    transferKindLabel: IsImageFilePath(path) ? "图片" : "文件");
                sentCount++;
            }
            catch (Exception ex)
            {
                failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (sentCount == 0 && failed.Count > 0)
        {
            throw new InvalidOperationException($"剪贴板内容发送失败：{string.Join("；", failed)}");
        }

        if (failed.Count > 0)
        {
            _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    _notificationService.Show("剪贴板发送部分失败", string.Join("；", failed), NotificationType.Warning);
                }
                catch
                {
                }
            });
        }

        return sentCount;
    }

    public async Task RespondToFileRequestAsync(string requestId, bool accepted, string? savePath = null)
    {
        if (!_pendingIncomingFileRequests.TryGetValue(requestId, out var requestContext))
        {
            return;
        }

        if (accepted && !string.IsNullOrEmpty(savePath))
        {
            _pendingDownloads[requestId] = savePath;
        }

        await SendBooleanResponseAsync(requestContext.Sender, PacketTypes.FileResponse, requestId, accepted);

        if (_pendingIncomingFileRequests.TryRemove(requestId, out _))
        {
            var statusContent = accepted
                ? "已同意接收文件请求。"
                : "已拒绝文件请求。";
            await SaveChatRecordAsync(
                requestContext.Sender,
                DeviceChatDirection.System,
                DeviceChatEntryType.TransferStatus,
                content: statusContent,
                fileName: requestContext.FileName,
                filePath: savePath ?? string.Empty,
                fileSize: requestContext.FileSize,
                requestId: requestId,
                status: accepted ? "accepted" : "rejected");
        }
    }

    public async Task HandleIncomingFileRequestAsync(PacketMetadata packet, DeviceModel sender)
    {
        if (string.IsNullOrWhiteSpace(packet.RequestId))
        {
            return;
        }

        try
        {
            _pendingIncomingFileRequests[packet.RequestId] = new IncomingFileRequestContext
            {
                FileName = packet.FileName,
                FileSize = packet.Size,
                Sender = CloneDeviceModel(sender)
            };

            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.FileRequest,
                content: "收到文件传输请求。",
                fileName: packet.FileName,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "requested");

            FileRequestReceived?.Invoke(
                this,
                new FileTransferRequestEventArgs(
                    packet.RequestId,
                    packet.FileName,
                    packet.Size,
                    CloneDeviceModel(sender)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FileReq] Handle request error: {ex}");
            _pendingIncomingFileRequests.TryRemove(packet.RequestId, out _);
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.System,
                DeviceChatEntryType.TransferStatus,
                content: $"处理文件请求失败：{ex.Message}",
                fileName: packet.FileName,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "failed");
            try
            {
                await SendBooleanResponseAsync(sender, PacketTypes.FileResponse, packet.RequestId, false);
            }
            catch
            {
            }
        }
    }

    public async Task HandleIncomingFileResponseAsync(PacketMetadata packet, DeviceModel sender)
    {
        _requestTracker.Resolve(packet.RequestId, packet.Accepted);
        await SaveChatRecordAsync(
            sender,
            DeviceChatDirection.Incoming,
            DeviceChatEntryType.TransferStatus,
            content: packet.Accepted
                ? "对方已同意文件传输请求。"
                : "对方已拒绝文件传输请求。",
            requestId: packet.RequestId,
            status: packet.Accepted ? "accepted" : "rejected");
    }

    public async Task<bool> HandleIncomingFileTransferAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        if (!string.Equals(packet.Type, PacketTypes.FileTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_pendingDownloads.TryRemove(packet.RequestId, out var savePath))
        {
            await HandleIncomingFileTransferToPendingPathAsync(packet, dataStream, sender, savePath);
            return true;
        }

        return await TryHandleManualFileTransferAsync(packet, dataStream, sender);
    }

    public void CancelAll()
    {
        _pendingDownloads.Clear();
        _pendingIncomingFileRequests.Clear();

        foreach (var kv in _sendingTransferToasts.ToArray())
        {
            if (_sendingTransferToasts.TryRemove(kv.Key, out var handle))
            {
                try
                {
                    handle.Fail("传输已取消");
                }
                catch
                {
                }
            }
        }

        foreach (var kv in _receivingTransferToasts.ToArray())
        {
            if (_receivingTransferToasts.TryRemove(kv.Key, out var handle))
            {
                try
                {
                    handle.Fail("传输已取消");
                }
                catch
                {
                }
            }
        }
    }

    public void Dispose()
    {
        CancelAll();
    }
}
