using PluginCore;

namespace Core.Services.DeviceCommunication.FileTransfer;

public sealed class IncomingFileRequestContext
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DeviceModel Sender { get; set; } = new();
}
