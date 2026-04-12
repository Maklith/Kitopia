namespace Core.Services.DeviceCommunication.Security;

public enum ProtocolErrorCode
{
    Unknown = 0,
    RouteNotFound = 1,
    InvalidFrame = 2,
    SecurityValidationFailed = 3,
    ChannelNotFound = 4
}

public readonly record struct ProtocolError(ProtocolErrorCode Code, string Message);
