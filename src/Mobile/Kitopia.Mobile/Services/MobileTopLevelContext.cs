using Avalonia.Controls;

namespace Kitopia.Mobile.Services;

public sealed class MobileTopLevelContext
{
    public TopLevel? CurrentTopLevel { get; set; }
    public bool IsActivityActive { get; set; } = true;
    public Func<MobileTextPromptRequest, Task>? TextPromptHandler { get; set; }

    public async Task<string?> PromptTextAsync(string title, string prompt, string? initialValue)
    {
        var handler = TextPromptHandler;
        if (handler is null)
        {
            return null;
        }

        var request = new MobileTextPromptRequest(title, prompt, initialValue);
        await handler(request);
        return await request.Completion;
    }

    /// <summary>
    /// Set while this app is showing its own file picker / save dialog. On Android the picker
    /// runs as a separate Activity which triggers OnPause; without this guard the communication
    /// host would be torn down (discovery device list cleared) and the subsequent send/accept
    /// would fail because the peer can no longer be resolved.
    /// </summary>
    public bool SuppressPause { get; set; }
}
