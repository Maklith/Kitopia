using Core.Services.DeviceCommunication.Messages;

namespace Core.Services.DeviceCommunication.Application;

public interface IIncomingMessageSink
{
    ValueTask PublishAsync(AppMessage message, CancellationToken cancellationToken = default);
    ValueTask PublishEventAsync(DeviceMessageEvent messageEvent, CancellationToken cancellationToken = default);
}
