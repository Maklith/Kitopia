using System.Net;

namespace Core.Services.DeviceCommunication.Routing;

public readonly record struct MessageContext(
    LocalDataTransportProtocol Protocol,
    IPEndPoint RemoteEndPoint,
    string RemoteIdentityPublicKey);
