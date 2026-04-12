using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Net;

namespace Core.Services.DeviceCommunication;

public interface ILocalDataStreamControl
{
    ValueTask HandleAsync(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        PipeReader payloadReader,
        CancellationToken cancellationToken = default);

    Task SendMessageAsync(
        LocalDataSendContext sendContext,
        string message,
        CancellationToken cancellationToken = default);

    Task SendCommandAsync(
        LocalDataSendContext sendContext,
        string route,
        string command,
        IReadOnlyDictionary<string, string?>? metadata = null,
        Guid? channelId = null,
        string? contentType = null,
        string? message = null,
        CancellationToken cancellationToken = default);

    Task SendFileAsync(
        LocalDataSendContext sendContext,
        Stream stream,
        string? fileName = null,
        int framePayloadSize = 64 * 1024,
        CancellationToken cancellationToken = default);
}

public readonly record struct LocalDataSendContext(
    ILocalDataListener Listener,
    LocalDataTransportProtocol Protocol,
    IPEndPoint RemoteEndPoint,
    string RemoteIdentityPublicKey);
