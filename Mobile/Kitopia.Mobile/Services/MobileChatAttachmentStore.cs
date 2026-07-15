using Avalonia.Platform.Storage;
using Kitopia.Feature.DeviceCommunication.Application;

namespace Kitopia.Mobile.Services;

public sealed class MobileChatAttachmentStore : IChatAttachmentStore
{
    private readonly MobileTopLevelContext _topLevel;
    private readonly string _cacheDirectory;
    private readonly string _incomingRootDirectory;

    public MobileChatAttachmentStore(MobileTopLevelContext topLevel)
    {
        _topLevel = topLevel;
        _cacheDirectory = Path.Combine(Path.GetTempPath(), "Kitopia.Mobile", "picker-cache");
        _incomingRootDirectory = GetIncomingRootDirectory();
        Directory.CreateDirectory(_cacheDirectory);
    }

    public async Task<IReadOnlyList<string>> PickFilesToSendAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = _topLevel.CurrentTopLevel?.StorageProvider;
        if (provider?.CanOpen != true)
        {
            return [];
        }

        _topLevel.SuppressPause = true;
        try
        {
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的文件",
                AllowMultiple = false
            });
            cancellationToken.ThrowIfCancellationRequested();
            var file = files.Count > 0 ? files[0] : null;
            if (file is null)
            {
                return [];
            }

            await using var source = await file.OpenReadAsync();
            var tempPath = MobileFileCache.CreatePath(_cacheDirectory, file.Name);
            await using var target = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                useAsync: true);
            await source.CopyToAsync(target, cancellationToken);
            return [tempPath];
        }
        finally
        {
            _topLevel.SuppressPause = false;
        }
    }

    public async Task<ChatFileSaveTarget?> PickSaveTargetAsync(
        string suggestedFileName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var provider = _topLevel.CurrentTopLevel?.StorageProvider;
        _topLevel.SuppressPause = true;
        try
        {
            if (provider?.CanSave == true)
            {
                var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "保存接收的文件",
                    SuggestedFileName = suggestedFileName
                });
                cancellationToken.ThrowIfCancellationRequested();
                if (file?.Path is null)
                {
                    return null;
                }

                if (file.Path.IsFile)
                {
                    return ChatFileSaveTarget.FromLocalPath(file.Path.LocalPath);
                }

                return new ChatFileSaveTarget(
                    file.Path.ToString(),
                    null,
                    _ => new ValueTask<Stream>(file.OpenWriteAsync()));
            }

            return ChatFileSaveTarget.FromLocalPath(
                MobileReceiveSavePathResolver.ResolveIncomingPath(_incomingRootDirectory, suggestedFileName));
        }
        finally
        {
            _topLevel.SuppressPause = false;
        }
    }

    private static string GetIncomingRootDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            return Path.Combine(documents, "Kitopia");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(appData) ? Path.GetTempPath() : appData,
            "Kitopia");
    }
}
