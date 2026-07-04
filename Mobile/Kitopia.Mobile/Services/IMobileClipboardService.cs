namespace Kitopia.Mobile.Services;

public interface IMobileClipboardService
{
    Task<string?> GetTextAsync(CancellationToken cancellationToken = default);
    Task SetTextAsync(string text, CancellationToken cancellationToken = default);
}
