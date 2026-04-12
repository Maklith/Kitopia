namespace Core.Services.DeviceCommunication.Handlers;

public sealed class ImageTransferPolicy
{
    public const long DirectSendThresholdBytes = 5L * 1024L * 1024L;

    public bool ShouldDirectSend(long imageSizeBytes)
    {
        return imageSizeBytes > 0 && imageSizeBytes <= DirectSendThresholdBytes;
    }
}
