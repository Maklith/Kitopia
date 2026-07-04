using Avalonia.Platform.Storage;

namespace Kitopia.Mobile.Services;

public sealed class AvaloniaMobileFilePickerService : IMobileFilePickerService
{
    private readonly MobileTopLevelContext _topLevelContext;
    private readonly string _cacheDirectory;

    public AvaloniaMobileFilePickerService(MobileTopLevelContext topLevelContext)
    {
        _topLevelContext = topLevelContext;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "Kitopia.Mobile", "picker-cache");
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        var storageProvider = _topLevelContext.CurrentTopLevel?.StorageProvider;
        if (storageProvider?.CanSave == true)
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save incoming file",
                SuggestedFileName = suggestedFileName
            });
            if (file?.Path is not null && file.Path.IsFile)
            {
                return file.Path.LocalPath;
            }
        }

        var fallbackDirectory = GetFallbackSaveDirectory();
        Directory.CreateDirectory(fallbackDirectory);
        return Path.Combine(fallbackDirectory, suggestedFileName);
    }

    public async Task<MobilePickedFile?> PickFileToSendAsync(
        MobilePickedFileKind kind,
        CancellationToken cancellationToken = default)
    {
        var storageProvider = _topLevelContext.CurrentTopLevel?.StorageProvider;
        if (storageProvider?.CanOpen != true)
        {
            return null;
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = kind == MobilePickedFileKind.Image ? "Pick image" : "Pick file",
            AllowMultiple = false
        });
        var file = files.FirstOrDefault();
        if (file is null)
        {
            return null;
        }

        await using var source = await file.OpenReadAsync();
        var extension = Path.GetExtension(file.Name);
        var tempPath = Path.Combine(_cacheDirectory, $"{Guid.NewGuid():N}{extension}");
        await using (var target = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         useAsync: true))
        {
            await source.CopyToAsync(target, cancellationToken);
        }

        var properties = await file.GetBasicPropertiesAsync();
        return new MobilePickedFile(
            file.Name,
            ResolveContentType(file.Name, kind),
            properties.Size.HasValue ? checked((long?)properties.Size.Value) : null,
            tempPath);
    }

    private static string GetFallbackSaveDirectory()
    {
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(downloads))
        {
            return Path.Combine(downloads, "Kitopia");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Kitopia",
            "Incoming");
    }

    private static string ResolveContentType(string fileName, MobilePickedFileKind kind)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (kind == MobilePickedFileKind.Image)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "image/*"
            };
        }

        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream"
        };
    }
}
