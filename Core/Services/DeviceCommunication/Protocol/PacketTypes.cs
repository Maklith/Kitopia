namespace Core.Services.DeviceCommunication.Protocol;

public static class PacketTypes
{
    public const string Message = "Message";
    public const string ClipboardText = "ClipboardText";
    public const string ClipboardSyncRequest = "ClipboardSyncReq";
    public const string ClipboardSyncResponse = "ClipboardSyncResp";
    public const string FileRequest = "FileReq";
    public const string FileResponse = "FileResp";
    public const string FileTransfer = "FileTransfer";
    public const string Legacy = "Legacy";
}
