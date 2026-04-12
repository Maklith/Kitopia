namespace Core.Services.DeviceCommunication.Security;

public readonly record struct SecurityContext(string SessionId, string RemoteIdentityPublicKey);
