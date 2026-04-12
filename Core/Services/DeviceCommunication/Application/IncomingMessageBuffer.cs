using System.Threading.Channels;
using Core.Services.DeviceCommunication.Messages;

namespace Core.Services.DeviceCommunication.Application;

public sealed class IncomingMessageBuffer : IIncomingMessageSink
{
    private readonly Channel<IncomingMessageEvent> _channel = Channel.CreateBounded<IncomingMessageEvent>(1024);

    public ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(new IncomingMessageEvent(message), cancellationToken);
    }

    public IAsyncEnumerable<IncomingMessageEvent> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
