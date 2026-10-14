using Kitopia.Feature.DeviceCommunication.Messages;

namespace Kitopia.Feature.DeviceCommunication.Application;

public interface IIncomingMessageSink
{
    ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default);
    ValueTask PublishEventAsync(DeviceMessageEvent messageEvent, CancellationToken cancellationToken = default);
}
