namespace Kitopia.Feature.DeviceCommunication.Application;

public sealed record ChatFileSaveTarget(
    string DisplayPath,
    string? LocalPath,
    Func<CancellationToken, ValueTask<Stream>> OpenWriteAsync)
{
    public static ChatFileSaveTarget FromLocalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new ChatFileSaveTarget(
            path,
            path,
            _ => new ValueTask<Stream>(new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true)));
    }
}

public interface IChatAttachmentStore
{
    Task<IReadOnlyList<string>> PickFilesToSendAsync(CancellationToken cancellationToken = default);

    Task<ChatFileSaveTarget?> PickSaveTargetAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default);

    byte[]? GetFileIconPng(string path) => null;
}
