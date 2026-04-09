using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.FileTransfer;

public sealed partial class FileTransferService
{
    private void PublishTransferProgress(
        DeviceModel peer,
        string requestId,
        string fileName,
        long transferredBytes,
        long totalBytes,
        bool isSending)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        TransferProgress?.Invoke(
            this,
            new FileTransferProgressEventArgs(
                requestId,
                fileName,
                transferredBytes,
                totalBytes,
                isSending,
                CloneDeviceModel(peer)));
    }

    private void PublishTransferCompleted(
        DeviceModel peer,
        string requestId,
        string fileName,
        long fileSize,
        string filePath,
        bool isSending)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        TransferCompleted?.Invoke(
            this,
            new FileTransferCompletedEventArgs(
                requestId,
                fileName,
                fileSize,
                filePath,
                isSending,
                CloneDeviceModel(peer)));
    }

    private PacketMetadata CreatePacketMetadata(
        string type,
        string content = "",
        string requestId = "",
        string fileName = "",
        long size = 0,
        bool accepted = false)
    {
        return new PacketMetadata
        {
            Type = type,
            Content = content,
            RequestId = requestId,
            FileName = fileName,
            Size = size,
            Accepted = accepted,
            SenderPort = _getAdvertisedPort(),
            SenderId = _getLocalDeviceId().ToString(),
            SenderName = _getLocalDeviceName()
        };
    }

    private Task SendPacketMetadataAsync(DeviceModel target, PacketMetadata metadata)
    {
        return _transportService.SendAsync(target, metadata, Stream.Null);
    }

    private Task SendBooleanResponseAsync(DeviceModel target, string responseType, string requestId, bool accepted)
    {
        var responseMeta = CreatePacketMetadata(
            type: responseType,
            requestId: requestId,
            accepted: accepted);
        return SendPacketMetadataAsync(target, responseMeta);
    }

    private Action<long>? CreateTransferProgressHandler(
        string requestId,
        bool isSending,
        string fileName,
        long totalBytes,
        string remoteName,
        DeviceModel? peer = null)
    {
        if (totalBytes <= 0)
        {
            return null;
        }

        long transferredBytes = 0;
        int lastPercent = -1;
        var lastUpdate = DateTime.MinValue;
        var progressLock = new object();

        return bytes =>
        {
            var copied = Interlocked.Add(ref transferredBytes, bytes);
            var percent = (int)Math.Min(100, copied * 100d / totalBytes);
            var now = DateTime.UtcNow;

            lock (progressLock)
            {
                if (percent == lastPercent && now - lastUpdate < TransferToastUpdateInterval)
                {
                    return;
                }

                lastPercent = percent;
                lastUpdate = now;
            }

            UpdateTransferToastProgress(requestId, isSending, fileName, copied, totalBytes, remoteName);
            if (peer is not null)
            {
                PublishTransferProgress(peer, requestId, fileName, copied, totalBytes, isSending);
            }
        };
    }

    private async Task SaveChatRecordAsync(
        DeviceModel? peer,
        DeviceChatDirection direction,
        DeviceChatEntryType entryType,
        string content = "",
        string fileName = "",
        string filePath = "",
        long fileSize = 0,
        string requestId = "",
        string status = "")
    {
        try
        {
            var peerId = peer?.Id ?? string.Empty;
            var peerAddress = peer?.Address?.ToString() ?? string.Empty;
            var peerPort = peer?.Port ?? 0;

            await _chatHistoryService.AppendAsync(new DeviceChatMessage
            {
                PeerKey = DeviceChatPeerKey.Build(peerId, peerAddress, peerPort),
                PeerId = peerId,
                PeerName = GetDeviceDisplayName(peer),
                PeerAddress = peerAddress,
                PeerPort = peerPort,
                Direction = direction,
                EntryType = entryType,
                Content = content ?? string.Empty,
                FileName = fileName ?? string.Empty,
                FilePath = filePath ?? string.Empty,
                FileSize = fileSize,
                RequestId = requestId ?? string.Empty,
                Status = status ?? string.Empty,
                TimestampUtc = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ChatHistory] Save failed: {ex}");
        }
    }

    private static async Task DrainRemainingDataAsync(Stream stream)
    {
        if (!stream.CanRead)
        {
            return;
        }

        var buffer = new byte[8192];
        while (await stream.ReadAsync(buffer, 0, buffer.Length) > 0)
        {
        }
    }

    private static bool IsImageFilePath(string path)
    {
        var extension = Path.GetExtension(path);
        return !string.IsNullOrWhiteSpace(extension) && ImageFileExtensions.Contains(extension);
    }

    private void ShowIncomingFileSavedToast(DeviceModel sender, string savedPath)
    {
        var senderName = GetDeviceDisplayName(sender);
        _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _notificationService.Show(
                    "文件接收成功",
                    $"来自 {senderName} 的文件已保存至: {savedPath}",
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileTransfer] Success toast error: {ex}");
            }
        });
    }

    private static DeviceModel CloneDeviceModel(DeviceModel source)
    {
        return new DeviceModel
        {
            Id = source.Id,
            Name = source.Name,
            CustomName = source.CustomName,
            Address = source.Address,
            Port = source.Port,
            LastSeen = source.LastSeen
        };
    }

    private static string GetDeviceDisplayName(DeviceModel? device)
    {
        if (device is null)
        {
            return "未知设备";
        }

        if (!string.IsNullOrWhiteSpace(device.CustomName))
        {
            return device.CustomName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.Name))
        {
            return device.Name.Trim();
        }

        return device.Address.ToString();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var unitIndex = 0;
        double value = bytes;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        if (unitIndex == 0)
        {
            return $"{bytes:N0} B";
        }

        return $"{value:0.##} {units[unitIndex]}";
    }

    private ConcurrentDictionary<string, IToastProgressHandle> GetTransferToastMap(bool isSending)
    {
        return isSending ? _sendingTransferToasts : _receivingTransferToasts;
    }

    private static (string Action, string Direction, string Title) GetTransferToastText(bool isSending, bool completed)
    {
        if (completed)
        {
            return isSending ? ("已发送", "到", "文件发送") : ("已接收", "从", "文件接收");
        }

        return isSending ? ("发送", "到", "文件发送") : ("接收", "从", "文件接收");
    }

    private void StartTransferToast(string requestId, bool isSending, string fileName, long totalBytes, string remoteName)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var (action, direction, title) = GetTransferToastText(isSending, completed: false);
        var detail = totalBytes > 0
            ? $"{FormatFileSize(0)} / {FormatFileSize(totalBytes)}"
            : "准备中...";
        var handle = _notificationService.ShowProgress(
            title,
            $"{action} {fileName} {direction} {remoteName} ({detail})",
            NotificationType.Information,
            initialProgress: 0,
            isIndeterminate: totalBytes <= 0);

        GetTransferToastMap(isSending)[requestId] = handle;
    }

    private void UpdateTransferToastProgress(
        string requestId,
        bool isSending,
        string fileName,
        long transferredBytes,
        long totalBytes,
        string remoteName)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var map = GetTransferToastMap(isSending);
        if (!map.TryGetValue(requestId, out var handle))
        {
            return;
        }

        var (action, direction, _) = GetTransferToastText(isSending, completed: false);
        if (totalBytes > 0)
        {
            var progress = Math.Min(100, transferredBytes * 100d / totalBytes);
            handle.Update(
                progress: progress,
                text:
                $"{action} {fileName} {direction} {remoteName} ({FormatFileSize(transferredBytes)} / {FormatFileSize(totalBytes)})");
            return;
        }

        handle.Update(
            text: $"{action} {fileName} {direction} {remoteName} ({FormatFileSize(transferredBytes)})",
            isIndeterminate: true);
    }

    private void CompleteTransferToast(
        string requestId,
        bool isSending,
        string fileName,
        long totalBytes,
        string remoteName)
    {
        var map = GetTransferToastMap(isSending);
        if (!map.TryRemove(requestId, out var handle))
        {
            return;
        }

        var (action, direction, _) = GetTransferToastText(isSending, completed: true);
        var sizeText = totalBytes > 0 ? $" ({FormatFileSize(totalBytes)})" : string.Empty;
        handle.Complete($"{action} {fileName}{sizeText} {direction} {remoteName}");
    }

    private void FailTransferToast(string requestId, string reason, bool isSending)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        var map = GetTransferToastMap(isSending);
        if (!map.TryRemove(requestId, out var handle))
        {
            return;
        }

        handle.Fail($"传输中断：{reason}");
    }
}
