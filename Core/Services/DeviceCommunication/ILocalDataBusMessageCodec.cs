using System;

namespace Core.Services.DeviceCommunication;

public interface ILocalDataBusMessageCodec
{
    Type MessageType { get; }
    string Route { get; }

    bool CanHandleCommand(string? command);
    bool TryDecode(LocalDataBusEnvelopeReceivedEventArgs envelopeArgs, out object message);
    bool TryEncode(object message, out LocalDataBusEnvelope envelope);
}

public interface ILocalDataBusMessageCodec<TMessage> : ILocalDataBusMessageCodec
{
    bool TryDecode(LocalDataBusEnvelopeReceivedEventArgs envelopeArgs, out TMessage message);
    bool TryEncode(TMessage message, out LocalDataBusEnvelope envelope);
}
