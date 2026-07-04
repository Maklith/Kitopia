namespace Kitopia.Mobile.Services;

public sealed class MobilePickedFile : IAsyncDisposable
{
    private readonly string _tempPath;

    public MobilePickedFile(string displayName, string contentType, long? sizeBytes, string tempPath)
    {
        DisplayName = displayName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        _tempPath = tempPath;
    }

    public string DisplayName { get; }
    public string ContentType { get; }
    public long? SizeBytes { get; }

    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        Stream stream = new FileStream(
            _tempPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            useAsync: true);
        return Task.FromResult(stream);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(_tempPath))
            {
                File.Delete(_tempPath);
            }
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
    }
}
