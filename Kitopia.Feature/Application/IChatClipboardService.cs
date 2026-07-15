namespace Kitopia.Feature.DeviceCommunication.Application;

public sealed record ChatClipboardContent(string? Text, byte[]? ImagePng, IReadOnlyList<string> Files);

public interface IChatClipboardService
{
    ValueTask<ChatClipboardContent> ReadAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> SetTextAsync(string text, CancellationToken cancellationToken = default);
    ValueTask<bool> SetImageAsync(ReadOnlyMemory<byte> imagePng, CancellationToken cancellationToken = default);
}
