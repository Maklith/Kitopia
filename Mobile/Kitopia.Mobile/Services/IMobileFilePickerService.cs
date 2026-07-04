namespace Kitopia.Mobile.Services;

public interface IMobileFilePickerService
{
    Task<string?> PickSavePathAsync(string suggestedFileName, CancellationToken cancellationToken = default);
    Task<MobilePickedFile?> PickFileToSendAsync(MobilePickedFileKind kind, CancellationToken cancellationToken = default);
}
