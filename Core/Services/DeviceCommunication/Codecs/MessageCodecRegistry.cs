using Core.Services.DeviceCommunication.Messages;

namespace Core.Services.DeviceCommunication.Codecs;

public sealed class MessageCodecRegistry
{
    private readonly Dictionary<(string Route, string Command), IMessageCodec> _byRouteAndCommand =
        new();
    private readonly Dictionary<Type, IMessageCodec> _byType = new();

    public MessageCodecRegistry(IEnumerable<IMessageCodec> codecs)
    {
        foreach (var codec in codecs)
        {
            var key = (codec.Route, codec.Command);
            if (!_byRouteAndCommand.TryAdd(key, codec))
            {
                throw new InvalidOperationException($"Duplicate codec key: {codec.Route}/{codec.Command}");
            }

            if (!_byType.TryAdd(codec.MessageType, codec))
            {
                throw new InvalidOperationException($"Duplicate codec type: {codec.MessageType.FullName}");
            }
        }
    }

    public bool TryGetByMessage(AppMessage message, out IMessageCodec codec)
    {
        return _byType.TryGetValue(message.GetType(), out codec!);
    }

    public bool TryGetByEnvelope(string route, string command, out IMessageCodec codec)
    {
        return _byRouteAndCommand.TryGetValue((route, command), out codec!);
    }
}
