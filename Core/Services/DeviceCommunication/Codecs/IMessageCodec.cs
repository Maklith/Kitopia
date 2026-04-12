using Core.Services.DeviceCommunication.Messages;
using Core.Services.DeviceCommunication.Protocol;

namespace Core.Services.DeviceCommunication.Codecs;

public interface IMessageCodec
{
    string Route { get; }
    string Command { get; }
    Type MessageType { get; }

    bool TryEncode(AppMessage message, out DataEnvelope envelope);
    bool TryDecode(DataEnvelope envelope, out AppMessage message);
}
