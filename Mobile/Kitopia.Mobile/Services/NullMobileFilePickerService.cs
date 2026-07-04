namespace Kitopia.Mobile.Services;

public sealed class NullMobileFilePickerService : IMobileFilePickerService
{
    public Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default)
    {
        _ = suggestedFileName;
        _ = cancellationToken;
        return Task.FromResult<string?>(null);
    }

    public Task<MobilePickedFile?> PickFileToSendAsync(MobilePickedFileKind kind, CancellationToken cancellationToken = default)
    {
        _ = kind;
        _ = cancellationToken;
        return Task.FromResult<MobilePickedFile?>(null);
    }
}
