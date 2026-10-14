using System;
using System.Threading;
using System.Threading.Tasks;
using Kitopia.Feature.DeviceCommunication.Application;
using OpenCvSharp;
using PluginCore;

namespace Kitopia.Desktop.Services;

public sealed class DesktopChatClipboardService : IChatClipboardService
{
    private readonly IClipboardService _clipboardService;

    public DesktopChatClipboardService(IClipboardService clipboardService)
    {
        _clipboardService = clipboardService;
    }

    public ValueTask<ChatClipboardContent> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[]? imagePng = null;
        if (_clipboardService.HasImage())
        {
            using var image = _clipboardService.GetImage();
            if (image is { } && !image.Empty())
            {
                Cv2.ImEncode(".png", image, out imagePng);
            }
        }

        return ValueTask.FromResult(new ChatClipboardContent(
            _clipboardService.HasText() ? _clipboardService.GetText() : null,
            imagePng,
            _clipboardService.HasFiles() ? _clipboardService.GetFiles() : []));
    }

    public ValueTask<bool> SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_clipboardService.SetText(text));
    }

    public async ValueTask<bool> SetImageAsync(
        ReadOnlyMemory<byte> imagePng,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var image = Cv2.ImDecode(imagePng.ToArray(), ImreadModes.Unchanged);
        if (image.Empty())
        {
            return false;
        }

        return await _clipboardService.SetImageAsync(new ScreenCaptureResult { Source = image });
    }
}
