namespace Core.Services.DeviceCommunication.Protocol;

public readonly record struct FrameHeader(
    byte Version,
    byte FrameType,
    byte Flags,
    Guid ChannelId,
    int PayloadLength);
