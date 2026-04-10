using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Core.Services.DeviceCommunication.Protocol;
using Core.Services.DeviceCommunication.Requests;
using PluginCore;

namespace Core.Services.DeviceCommunication.FileTransfer;

public sealed partial class FileTransferService
{
    private async Task RequestFileTransferInternalAsync(DeviceModel target, string filePath, string transferKindLabel)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(filePath);
        }

        var fileInfo = new FileInfo(filePath);
        var requestId = Guid.NewGuid().ToString();
        var requestMeta = CreatePacketMetadata(
            type: PacketTypes.FileRequest,
            requestId: requestId,
            fileName: fileInfo.Name,
            size: fileInfo.Length);

        Task SaveFileRecordAsync(
            DeviceChatDirection direction,
            DeviceChatEntryType entryType,
            string content,
            string status)
        {
            return SaveChatRecordAsync(
                target,
                direction,
                entryType,
                content: content,
                fileName: fileInfo.Name,
                filePath: fileInfo.FullName,
                fileSize: fileInfo.Length,
                requestId: requestId,
                status: status);
        }

        try
        {
            var decision = await _requestTracker.WaitForBooleanResponseAsync(
                requestId,
                async () =>
                {
                    await SendPacketMetadataAsync(target, requestMeta);
                    await SaveFileRecordAsync(
                        DeviceChatDirection.Outgoing,
                        DeviceChatEntryType.FileRequest,
                        $"已发起{transferKindLabel}传输请求。",
                        "requested");
                },
                TimeSpan.FromMinutes(1),
                "无法跟踪文件传输请求。");

            switch (decision)
            {
                case RequestDecision.Accepted:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        $"对方已同意{transferKindLabel}传输请求。",
                        "accepted");
                    await SendFileTransferPayloadAsync(target, filePath, fileInfo, requestId);
                    await SaveFileRecordAsync(
                        DeviceChatDirection.Outgoing,
                        DeviceChatEntryType.File,
                        $"{transferKindLabel}传输完成。",
                        "completed");
                    return;

                case RequestDecision.TimedOut:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        $"对方未响应{transferKindLabel}传输请求。",
                        "timeout");
                    throw new TimeoutException("对方未在规定时间内响应。");

                default:
                    await SaveFileRecordAsync(
                        DeviceChatDirection.System,
                        DeviceChatEntryType.TransferStatus,
                        $"对方已拒绝{transferKindLabel}传输请求。",
                        "rejected");
                    return;
            }
        }
        catch (Exception ex)
        {
            FailTransferToast(requestId, ex.Message, isSending: true);
            await SaveFileRecordAsync(
                DeviceChatDirection.Outgoing,
                DeviceChatEntryType.File,
                $"{transferKindLabel}传输失败：{ex.Message}",
                "failed");
            throw;
        }
    }

    private async Task SendFileTransferPayloadAsync(DeviceModel target, string filePath, FileInfo fileInfo, string requestId)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var fileMeta = CreatePacketMetadata(
            type: PacketTypes.FileTransfer,
            requestId: requestId,
            fileName: fileInfo.Name,
            size: fileInfo.Length);
        var targetName = target.ToString();
        StartTransferToast(requestId, isSending: true, fileInfo.Name, fileInfo.Length, targetName);

        var onProgress = CreateTransferProgressHandler(
            requestId,
            isSending: true,
            fileName: fileInfo.Name,
            totalBytes: fileInfo.Length,
            remoteName: targetName,
            peer: target);

        try
        {
            await _transportService.SendAsync(target, fileMeta, fs, onProgress);
            CompleteTransferToast(requestId, isSending: true, fileInfo.Name, fileInfo.Length, targetName);
            PublishTransferCompleted(target, requestId, fileInfo.Name, fileInfo.Length, filePath, isSending: true);
        }
        catch (Exception ex)
        {
            NotifyTransferInterrupted(requestId, $"发送失败：{ex.Message}", isSending: true);
            throw;
        }
    }

    private async Task HandleIncomingFileTransferToPendingPathAsync(
        PacketMetadata packet,
        Stream dataStream,
        DeviceModel sender,
        string savePath)
    {
        var senderName = sender.ToString();
        var shouldShowExternalToast = _shouldShowExternalToastForSender(sender);
        if (shouldShowExternalToast)
        {
            StartTransferToast(packet.RequestId, isSending: false, packet.FileName, packet.Size, senderName);
        }

        var onProgress = CreateTransferProgressHandler(
            packet.RequestId,
            isSending: false,
            fileName: packet.FileName,
            totalBytes: packet.Size,
            remoteName: senderName,
            peer: sender);

        bool success = false;
        try
        {
            await using var fs = new FileStream(savePath, FileMode.Create, FileAccess.Write);
            await CopyStreamWithTimeoutAsync(dataStream, fs, TimeSpan.FromSeconds(10), onProgress: onProgress);

            if (packet.Size > 0 && fs.Length != packet.Size)
            {
                throw new IOException($"文件大小不匹配，期望 {packet.Size} 字节，实际 {fs.Length} 字节。");
            }

            success = true;
            if (shouldShowExternalToast)
            {
                CompleteTransferToast(packet.RequestId, isSending: false, packet.FileName, packet.Size, senderName);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dispatch] File save error: {ex}");
            NotifyTransferInterrupted(packet.RequestId, $"接收失败：{ex.Message}", isSending: false);
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.File,
                content: $"文件接收失败：{ex.Message}",
                fileName: packet.FileName,
                filePath: savePath,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "failed");
            try
            {
                File.Delete(savePath);
            }
            catch
            {
            }
        }

        if (!success)
        {
            return;
        }

        await SaveChatRecordAsync(
            sender,
            DeviceChatDirection.Incoming,
            DeviceChatEntryType.File,
            content: "文件接收完成。",
            fileName: packet.FileName,
            filePath: savePath,
            fileSize: packet.Size,
            requestId: packet.RequestId,
            status: "completed");
        PublishTransferCompleted(sender, packet.RequestId, packet.FileName, packet.Size, savePath, isSending: false);

        if (_shouldShowExternalToastForSender(sender))
        {
            ShowIncomingFileSavedToast(sender, savePath);
        }

        try
        {
            using var fsRead = new FileStream(savePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            StreamReceived?.Invoke(
                this,
                new DeviceStreamReceivedEventArgs(
                    sender,
                    fsRead,
                    JsonSerializer.Serialize(packet),
                    savePath));
        }
        catch
        {
        }
    }

    private async Task<bool> TryHandleManualFileTransferAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender)
    {
        var suggestedName = string.IsNullOrWhiteSpace(packet.FileName)
            ? "接收文件"
            : Path.GetFileName(packet.FileName);
        var manualSavePath = await _filePickerService.PickSaveFilePathAsync("保存接收到的文件", suggestedName);
        if (string.IsNullOrWhiteSpace(manualSavePath))
        {
            await DrainRemainingDataAsync(dataStream);
            return true;
        }

        try
        {
            await using var fs = new FileStream(manualSavePath, FileMode.Create, FileAccess.Write);
            await CopyStreamWithTimeoutAsync(dataStream, fs, TimeSpan.FromSeconds(10));
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.File,
                content: "文件接收完成。",
                fileName: packet.FileName,
                filePath: manualSavePath,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "completed");
            PublishTransferCompleted(
                sender,
                packet.RequestId,
                packet.FileName,
                packet.Size,
                manualSavePath,
                isSending: false);
            if (_shouldShowExternalToastForSender(sender))
            {
                ShowIncomingFileSavedToast(sender, manualSavePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Dispatch] Save manual stream error: {ex}");
            await SaveChatRecordAsync(
                sender,
                DeviceChatDirection.Incoming,
                DeviceChatEntryType.File,
                content: $"文件接收失败：{ex.Message}",
                fileName: packet.FileName,
                filePath: manualSavePath,
                fileSize: packet.Size,
                requestId: packet.RequestId,
                status: "failed");
            try
            {
                File.Delete(manualSavePath);
            }
            catch
            {
            }

            NotifyTransferInterrupted(packet.RequestId, $"接收失败：{ex.Message}", isSending: false);
            return true;
        }
    }

    private async Task CopyStreamWithTimeoutAsync(
        Stream source,
        Stream destination,
        TimeSpan timeout,
        int bufferSize = 8192,
        Action<long>? onProgress = null)
    {
        var buffer = new byte[bufferSize];
        using var cts = new CancellationTokenSource();

        while (true)
        {
            try
            {
                cts.CancelAfter(timeout);
                var bytesRead = await source.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                cts.CancelAfter(timeout);
                await destination.WriteAsync(buffer, 0, bytesRead, cts.Token);
                onProgress?.Invoke(bytesRead);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("数据传输超时。");
            }
        }
    }

    private void NotifyTransferInterrupted(string requestId, string reason, bool isSending)
    {
        FailTransferToast(requestId, reason, isSending);
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _toastService.Show(
                    "传输中断",
                    $"请求ID: {requestId}\n原因: {reason}\n方向: {(isSending ? "发送" : "接收")}",
                    NotificationType.Error);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Transfer] Interrupt toast error: {ex}");
            }

            if (!string.IsNullOrEmpty(requestId))
            {
                TransferInterrupted?.Invoke(this, new TransferInterruptionEventArgs(requestId, reason, isSending));
            }
        });
    }
}
