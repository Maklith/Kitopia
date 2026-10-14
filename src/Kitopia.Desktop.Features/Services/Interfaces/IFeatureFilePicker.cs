namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IFeatureFilePicker
{
    Task<IReadOnlyList<string>> PickFilesAsync(
        string title,
        bool allowMultiple,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> PickFoldersAsync(
        string title,
        bool allowMultiple,
        CancellationToken cancellationToken = default);
}
