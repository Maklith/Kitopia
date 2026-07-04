using Avalonia.Input.Platform;

namespace Kitopia.Mobile.Services;

public sealed class AvaloniaClipboardService : IMobileClipboardService
{
    private readonly MobileTopLevelContext _topLevelContext;

    public AvaloniaClipboardService(MobileTopLevelContext topLevelContext)
    {
        _topLevelContext = topLevelContext;
    }

    public async Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var clipboard = _topLevelContext.CurrentTopLevel?.Clipboard;
        return clipboard is null ? null : await clipboard.TryGetTextAsync();
    }

    public async Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var clipboard = _topLevelContext.CurrentTopLevel?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        await clipboard.SetTextAsync(text);
    }
}
