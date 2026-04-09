using System;
using System.IO;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.FileTransfer;

public interface IFileTransferService : IDisposable
{
    event EventHandler<FileTransferRequestEventArgs>? FileRequestReceived;
    event EventHandler<FileTransferProgressEventArgs>? TransferProgress;
    event EventHandler<FileTransferCompletedEventArgs>? TransferCompleted;
    event EventHandler<TransferInterruptionEventArgs>? TransferInterrupted;
    event EventHandler<DeviceStreamReceivedEventArgs>? StreamReceived;

    Task RequestFileTransferAsync(DeviceModel target, string? filePath = null);
    Task RequestImageTransferAsync(DeviceModel target, string? imagePath = null);
    Task<int> RequestClipboardTransferAsync(DeviceModel target);
    Task RespondToFileRequestAsync(string requestId, bool accepted, string? savePath = null);

    Task HandleIncomingFileRequestAsync(PacketMetadata packet, DeviceModel sender);
    Task HandleIncomingFileResponseAsync(PacketMetadata packet, DeviceModel sender);
    Task<bool> HandleIncomingFileTransferAsync(PacketMetadata packet, Stream dataStream, DeviceModel sender);

    void CancelAll();
}
