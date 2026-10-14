using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Kitopia.Feature.DeviceCommunication.Application;

namespace Kitopia.Mobile.Services;

public sealed class MobileChatClipboardService : IChatClipboardService
{
    private readonly MobileTopLevelContext _topLevel;
    private readonly string _cacheDirectory;

    public MobileChatClipboardService(MobileTopLevelContext topLevel)
    {
        _topLevel = topLevel;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "Kitopia.Mobile", "picker-cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async ValueTask<ChatClipboardContent> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _topLevel.CurrentTopLevel?.Clipboard;
        if (clipboard is null)
        {
            return new ChatClipboardContent(null, null, []);
        }

        string? text = null;
        try
        {
            text = await clipboard.TryGetTextAsync();
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<string> files = [];
        try
        {
            var storageItems = await clipboard.TryGetFilesAsync();
            files = await ResolveClipboardFilesAsync(storageItems, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        byte[]? imagePng = null;
        try
        {
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is not null)
            {
                await using var imageStream = new MemoryStream();
                bitmap.Save(imageStream);
                imagePng = imageStream.ToArray();
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new ChatClipboardContent(text, imagePng, files);
    }

    public async ValueTask<bool> SetTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _topLevel.CurrentTopLevel?.Clipboard;
        if (clipboard is null)
        {
            return false;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async ValueTask<bool> SetImageAsync(
        ReadOnlyMemory<byte> imagePng,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var clipboard = _topLevel.CurrentTopLevel?.Clipboard;
        if (clipboard is null || imagePng.IsEmpty)
        {
            return false;
        }

        try
        {
            await using var imageStream = new MemoryStream(imagePng.ToArray(), writable: false);
            using var bitmap = new Bitmap(imageStream);
            await clipboard.SetBitmapAsync(bitmap);
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<string>> ResolveClipboardFilesAsync(
        IReadOnlyList<IStorageItem>? storageItems,
        CancellationToken cancellationToken)
    {
        if (storageItems is null || storageItems.Count == 0)
        {
            return [];
        }

        var files = new List<string>(storageItems.Count);
        foreach (var item in storageItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item is not IStorageFile storageFile)
            {
                continue;
            }

            if (storageFile.Path.IsFile && File.Exists(storageFile.Path.LocalPath))
            {
                files.Add(storageFile.Path.LocalPath);
                continue;
            }

            string? cachePath = null;
            try
            {
                await using var source = await storageFile.OpenReadAsync();
                cachePath = MobileFileCache.CreatePath(_cacheDirectory, storageFile.Name);
                await using var target = new FileStream(
                    cachePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    64 * 1024,
                    useAsync: true);
                await source.CopyToAsync(target, cancellationToken);
                files.Add(cachePath);
            }
            catch
            {
                TryDeleteCacheFile(cachePath);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        return files;
    }

    private static void TryDeleteCacheFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
