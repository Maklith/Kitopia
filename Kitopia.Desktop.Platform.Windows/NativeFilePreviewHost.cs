using System.Drawing;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Web.WebView2.Core;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;

namespace Kitopia.Desktop.Platform.Windows;

public sealed class NativeFilePreviewHost : NativeControlHost
{
    public required string FilePath { get; init; }
    public event Action<string>? PreviewFailed;
    public event Action? PreviewReady;
    public event Action? CloseRequested;
    private User32.SafeHWND? _window;
    private CoreWebView2Controller? _controller;
    private IPreviewHandler? _handler;
    private IStream? _stream;
    private bool _closed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _closed = false;
        _window = User32.CreateWindowEx(0, "STATIC", "Kitopia file preview",
            User32.WindowStyles.WS_CHILD | User32.WindowStyles.WS_VISIBLE | User32.WindowStyles.WS_CLIPCHILDREN,
            0, 0, 1, 1, parent.Handle, default, default, default);
        var handle = new PlatformHandle(_window.DangerousGetHandle(), "HWND");
        _ = LoadAsync();
        return handle;
    }

    private async Task LoadAsync()
    {
        try
        {
            var extension = Path.GetExtension(FilePath).ToLowerInvariant();
            if (extension is not (".pdf" or ".mp4" or ".m4v" or ".webm" or ".mov" or ".ogv"
                or ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" or ".opus"
                or ".gif" or ".webp" or ".svg" or ".avif"))
            {
                LoadShellPreview();
                PreviewReady?.Invoke();
                return;
            }

            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(KitopiaPaths.AppRoot, "filePreviewBrowser"));
            if (_closed) return;
            var controller = await environment.CreateCoreWebView2ControllerAsync(_window!.DangerousGetHandle());
            if (_closed)
            {
                controller.Close();
                return;
            }
            _controller = controller;
            var isDark = ActualThemeVariant == ThemeVariant.Dark;
            controller.DefaultBackgroundColor = isDark ? Color.FromArgb(255, 36, 36, 36) : Color.White;
            var isAudio = extension is ".mp3" or ".m4a" or ".aac" or ".wav" or ".ogg" or ".flac" or ".opus";
            var isMedia = isAudio || extension is ".mp4" or ".m4v" or ".webm" or ".mov" or ".ogv";
            controller.AcceleratorKeyPressed += (_, e) =>
            {
                if (e.VirtualKey == 0x1B && e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown)
                {
                    e.Handled = true;
                    Dispatcher.UIThread.Post(() => CloseRequested?.Invoke());
                }
            };
            var browser = controller.CoreWebView2;
            browser.NavigationCompleted += async (_, e) =>
            {
                if (_closed) return;
                if (!e.IsSuccess)
                {
                    LogManager.Logger.Warning("文件预览导航失败: {Path}, {Status}, {Source}", FilePath, e.WebErrorStatus, browser.Source);
                    PreviewFailed?.Invoke("无法加载此文件的预览。");
                    return;
                }
                if (!isMedia)
                {
                    PreviewReady?.Invoke();
                    return;
                }
                try
                {
                    await browser.ExecuteScriptAsync("""
                        const media = document.querySelector('audio,video');
                        const ready = () => window.chrome.webview.postMessage('ready');
                        const failed = () => window.chrome.webview.postMessage('failed');
                        media.addEventListener('loadedmetadata', ready, { once: true });
                        media.addEventListener('error', failed, { once: true });
                        if (media.error) failed(); else if (media.readyState >= 1) ready();
                        """);
                }
                catch (Exception exception) when (exception is InvalidOperationException or COMException)
                {
                    if (!_closed) PreviewFailed?.Invoke("播放器加载失败，可以使用“打开文件”查看。");
                }
            };
            browser.WebMessageReceived += (_, e) =>
            {
                if (_closed) return;
                if (e.TryGetWebMessageAsString() == "ready") PreviewReady?.Invoke();
                else PreviewFailed?.Invoke("此音视频的编码暂不支持，或文件已损坏，可以使用“打开文件”播放。");
            };
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.Settings.AreBrowserAcceleratorKeysEnabled = false;
            browser.Settings.IsScriptEnabled = false;
            browser.Settings.IsStatusBarEnabled = false;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            browser.NewWindowRequested += (_, e) => e.Handled = true;
            browser.DownloadStarting += (_, e) => e.Cancel = true;
            var fileUri = isMedia
                ? "https://kitopia-preview.invalid/" + Uri.EscapeDataString(Path.GetFileName(FilePath))
                : new Uri(FilePath).AbsoluteUri;
            string? mediaDocumentUri = null;
            browser.NavigationStarting += (_, e) =>
            {
                if (isMedia && mediaDocumentUri is null && e.Uri.StartsWith("data:text/html;", StringComparison.Ordinal))
                {
                    mediaDocumentUri = e.Uri;
                    return;
                }
                if (!e.Uri.Equals(fileUri, StringComparison.OrdinalIgnoreCase) &&
                    !e.Uri.StartsWith(fileUri + "#", StringComparison.OrdinalIgnoreCase)) e.Cancel = true;
            };
            browser.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            browser.WebResourceRequested += (_, e) =>
            {
                if (!e.Request.Uri.Equals(fileUri, StringComparison.OrdinalIgnoreCase) && e.Request.Uri != mediaDocumentUri)
                    e.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            };
            ResizePreview();
            controller.IsVisible = true;
            if (isMedia)
            {
                browser.SetVirtualHostNameToFolderMapping("kitopia-preview.invalid", Path.GetDirectoryName(FilePath)!,
                    CoreWebView2HostResourceAccessKind.DenyCors);
                var tag = isAudio ? "audio" : "video";
                browser.NavigateToString($$"""
                    <!doctype html><html><head><meta charset="utf-8">
                    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; media-src https://kitopia-preview.invalid; style-src 'unsafe-inline'">
                    <style>
                    html,body { margin:0; height:100%; background:{{(isDark ? "#242424" : "#ffffff")}}; color:{{(isDark ? "#f9fafb" : "#111827")}}; font:14px 'Segoe UI',sans-serif; }
                    body { display:flex; flex-direction:column; align-items:center; justify-content:center; gap:24px; }
                    video { width:100%; height:100%; object-fit:contain; background:#000; }
                    audio { width:min(80%,520px); }
                    p { margin:0; max-width:80%; overflow-wrap:anywhere; text-align:center; }
                    </style></head><body>
                    {{(isAudio ? "<p>" + WebUtility.HtmlEncode(Path.GetFileName(FilePath)) + "</p>" : "")}}
                    <{{tag}} controls preload="metadata" src="{{fileUri}}"></{{tag}}>
                    </body></html>
                    """);
            }
            else browser.Navigate(fileUri);
        }
        catch (Exception exception)
        {
            if (_closed) return;
            LogManager.Logger.Warning(exception, "文件预览失败: {Path}", FilePath);
            PreviewFailed?.Invoke(exception is WebView2RuntimeNotFoundException
                ? "需要安装 Microsoft Edge WebView2 Runtime 才能预览此类文件。"
                : "此文件暂时无法预览，可以使用“打开文件”查看。");
        }
    }

    private void LoadShellPreview()
    {
        var identifier = new StringBuilder(128);
        var length = (uint)identifier.Capacity;
        ShlwApi.AssocQueryString(ShlwApi.ASSOCF.ASSOCF_INIT_DEFAULTTOSTAR, ShlwApi.ASSOCSTR.ASSOCSTR_SHELLEXTENSION,
            Path.GetExtension(FilePath), "{8895b1c6-b41f-4c1c-a562-0d564250836f}", identifier, ref length).ThrowIfFailed();
        var type = Type.GetTypeFromCLSID(Guid.Parse(identifier.ToString()), throwOnError: true)!;
        _handler = (IPreviewHandler)Activator.CreateInstance(type)!;
        if (_handler is IInitializeWithStream withStream)
        {
            ShlwApi.SHCreateStreamOnFileEx(FilePath, STGM.STGM_READ | STGM.STGM_SHARE_DENY_NONE,
                0, false, null, out _stream).ThrowIfFailed();
            withStream.Initialize(_stream, STGM.STGM_READ).ThrowIfFailed();
        }
        else if (_handler is IInitializeWithFile withFile)
            withFile.Initialize(FilePath, STGM.STGM_READ).ThrowIfFailed();
        else if (_handler is IInitializeWithItem withItem)
        {
            var item = SHCreateItemFromParsingName<IShellItem>(FilePath);
            try { withItem.Initialize(item, STGM.STGM_READ).ThrowIfFailed(); }
            finally { Marshal.ReleaseComObject(item); }
        }
        else throw new NotSupportedException("预览处理器不支持文件初始化。");
        User32.GetClientRect(_window!, out var bounds);
        _handler.SetWindow(_window!, bounds).ThrowIfFailed();
        _handler.DoPreview().ThrowIfFailed();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty) Dispatcher.UIThread.Post(ResizePreview, DispatcherPriority.Loaded);
    }

    private void ResizePreview()
    {
        if (_window is null || _closed) return;
        User32.GetClientRect(_window, out var bounds);
        if (_controller is not null) _controller.Bounds = new Rectangle(0, 0, bounds.Width, bounds.Height);
        _handler?.SetRect(bounds);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _closed = true;
        _controller?.Close();
        _controller = null;
        if (_handler is not null)
        {
            _handler.Unload();
            Marshal.ReleaseComObject(_handler);
            _handler = null;
        }
        if (_stream is not null)
        {
            Marshal.ReleaseComObject(_stream);
            _stream = null;
        }
        _window?.Dispose();
        _window = null;
    }
}
