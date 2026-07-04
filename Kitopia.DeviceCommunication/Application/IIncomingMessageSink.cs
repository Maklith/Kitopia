using Kitopia.DeviceCommunication.Messages;

namespace Kitopia.DeviceCommunication.Application;

public interface IIncomingMessageSink
{
    ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default);
    ValueTask PublishEventAsync(DeviceMessageEvent messageEvent, CancellationToken cancellationToken = default);
}
