using System.Net;

namespace Core.Services.DeviceCommunication;

public sealed class LocalDataBusMessageReceivedEventArgs<TMessage> : EventArgs
{
    public LocalDataBusMessageReceivedEventArgs(
        LocalDataTransportProtocol protocol,
        IPEndPoint remoteEndPoint,
        TMessage message,
        DateTimeOffset timestampUtc)
    {
        Protocol = protocol;
        RemoteEndPoint = remoteEndPoint;
        Message = message;
        TimestampUtc = timestampUtc;
    }

    public LocalDataTransportProtocol Protocol { get; }
    public IPEndPoint RemoteEndPoint { get; }
    public TMessage Message { get; }
    public DateTimeOffset TimestampUtc { get; }
}
