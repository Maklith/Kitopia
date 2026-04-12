using System.IO.Pipelines;

namespace Core.Services.DeviceCommunication.Sessions;

public sealed class PayloadSession
{
    public PayloadSession(Guid channelId)
    {
        ChannelId = channelId;
        var pipe = new Pipe();
        Reader = pipe.Reader;
        Writer = pipe.Writer;
    }

    public Guid ChannelId { get; }
    public PipeReader Reader { get; }
    public PipeWriter Writer { get; }
}
