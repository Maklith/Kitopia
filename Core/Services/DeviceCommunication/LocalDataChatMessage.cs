namespace Core.Services.DeviceCommunication;

public sealed class LocalDataChatMessage
{
    public LocalDataChatMessage(string text)
    {
        Text = text;
    }

    public string Text { get; }
}
