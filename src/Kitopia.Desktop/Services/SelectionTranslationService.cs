#if WINDOWS
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.TextSelection;
using Kitopia.Desktop.Features.Services;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Translation;
using Kitopia.Desktop.Platform.Windows.ScreenCapture;
using Kitopia.Desktop.Windows;
using Kitopia.Feature.Localization;
using PluginCore;
using System.Text;
using Serilog;
using SharpHook;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Services;

public sealed class SelectionTranslationService : ISelectionTranslationService
{
    private static readonly ILogger Logger = LogManager.Logger.ForContext<SelectionTranslationService>();

    private const int AutomaticDelayMilliseconds = 350;
    private const int MaximumTextLength = 1000;

    private readonly SimpleGlobalHook _inputHook;
    private readonly IHotKetImpl _hotkeys;
    private readonly ITextSelectionService _selectionService;
    private readonly ISelectionTextTranslator _translationService;
    private readonly SelectionTranslationWindow _window;
    private readonly SemaphoreSlim _selectionReadGate = new(1, 1);
    private CancellationTokenSource? _requestCancellation;
    private CancellationTokenSource? _pendingAutomaticCancellation;
    private MousePressState? _lastMousePress;
    private TextSelectionSnapshot? _currentSelection;
    private int _requestVersion;
    private bool _started;
    private bool _pressedInsideWindow;

    internal Func<nint> GetForegroundWindow { get; set; } = () => (nint)User32.GetForegroundWindow();

    public SelectionTranslationService(
        SimpleGlobalHook inputHook,
        IHotKetImpl hotkeys,
        ITextSelectionService selectionService,
        ISelectionTextTranslator translationService,
        SelectionTranslationWindow window)
    {
        _inputHook = inputHook;
        _hotkeys = hotkeys;
        _selectionService = selectionService;
        _translationService = translationService;
        _window = window;
        _window.ViewModel.CloseRequested += CloseWindow;
        _window.ViewModel.RetryRequested += RetryCurrentSelection;
        _window.ViewModel.LanguageChanged += TranslateCurrentSelection;
        _window.ViewModel.ExcludeCurrentProcessRequested += ExcludeCurrentProcess;
    }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _inputHook.MousePressed += OnMousePressed;
        _inputHook.KeyPressed += OnKeyPressed;
        _inputHook.MouseWheel += OnMouseWheel;
    }

    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        var hotkey = ConfigManger.Config.selectionTranslationAutoHotKey;
        if (!hotkey.IsEnabled || (hotkey.Type == HotKeyType.Mouse && !ConfigManger.Config.mouseCapture) ||
            (_currentSelection is { } selection && !hotkey.CanExecuteInProcess(selection.ProcessName)))
        {
            CancelCurrentRequest();
            if (!_window.ViewModel.IsPinned) CloseWindow();
        }
    }

    public async Task TriggerManualAsync()
    {
        await TranslateFromSelectionAsync(manual: true).ConfigureAwait(false);
    }

    public Task TriggerAutomaticAsync(HotKeyModel hotKeyModel) => _started
        ? TranslateFromSelectionAsync(manual: false, hotKeyModel.TriggerPosition)
        : Task.CompletedTask;

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _inputHook.MousePressed -= OnMousePressed;
        _inputHook.KeyPressed -= OnKeyPressed;
        _inputHook.MouseWheel -= OnMouseWheel;
        CancelCurrentRequest();
        Dispatcher.UIThread.Post(CloseWindow);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        CancelPendingAutomaticTrigger();
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        CancelPendingAutomaticTrigger();
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        CancelPendingAutomaticTrigger();
        if ((ushort)e.Data.Button != 1) return;
        if (!User32.GetCursorPos(out var point)) return;

        var foreground = GetForegroundWindow();
        var windowBounds = TryGetWindowBounds(foreground, out var rect) ? rect : (PixelRect?)null;
        Volatile.Write(ref _lastMousePress,
            new MousePressState(foreground, windowBounds, new PixelPoint(point.X, point.Y)));

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var insideWindow = _window.IsVisible && IsInsideWindow(point.X, point.Y);
                    _pressedInsideWindow = insideWindow;
                    if (!insideWindow && _window.IsVisible && !_window.ViewModel.IsPinned)
                        CloseWindow();
                }
                catch (Exception exception)
                {
                    Logger.Warning(exception, "处理划词翻译鼠标按下事件失败");
                }
            });
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "投递划词翻译鼠标按下事件失败");
        }
    }

    private async Task TranslateFromSelectionAsync(bool manual, PixelPoint? releasePosition = null)
    {
        nint ownWindowHandle = 0;
        if (!manual)
        {
            var preCheck = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var pressedInside = _pressedInsideWindow;
                _pressedInsideWindow = false;
                var handle = _window.TryGetPlatformHandle()?.Handle ?? 0;
                var skip = _window.IsVisible &&
                           (_window.ViewModel.IsPinned ||
                            pressedInside ||
                            (releasePosition is { } rp && IsInsideWindow(rp.X, rp.Y)));
                return (Skip: skip, Handle: handle);
            });
            if (preCheck.Skip)
            {
                Logger.Debug("自动划词翻译跳过：窗口已固定或触发点位于翻译窗口内");
                return;
            }
            ownWindowHandle = preCheck.Handle;
        }

        PixelRect? sourceWindowBounds = null;
        TextSelectionBounds? selectionBounds = null;
        PixelPoint? pressPosition = null;
        var (version, cancellation, token) = BeginRequest(trackPendingAutomatic: !manual);
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == 0)
            {
                Logger.Debug("划词翻译跳过：没有前台窗口，手动：{Manual}，请求：{RequestVersion}", manual, version);
                return;
            }
            if (!manual && ownWindowHandle != 0 && foreground == ownWindowHandle)
            {
                Logger.Debug("自动划词翻译跳过：前台窗口为划词翻译窗口，请求：{RequestVersion}", version);
                return;
            }

            var press = !manual ? Volatile.Read(ref _lastMousePress) : null;
            if (!manual && HasWindowMovedOrResized(press, foreground))
            {
                Logger.Debug("自动划词翻译跳过：拖拽期间前台窗口发生移动或缩放，请求：{RequestVersion}", version);
                return;
            }

            if (releasePosition is null && User32.GetCursorPos(out var point))
                releasePosition = new PixelPoint(point.X, point.Y);

            if (!manual)
            {
                await Task.Delay(AutomaticDelayMilliseconds, token).ConfigureAwait(false);
                if (GetForegroundWindow() != foreground)
                {
                    Logger.Debug("划词翻译跳过：等待选区期间前台窗口变化，手动：{Manual}，请求：{RequestVersion}", manual, version);
                    return;
                }
                if (HasWindowMovedOrResized(press, foreground))
                {
                    Logger.Debug("自动划词翻译跳过：等待期间前台窗口发生移动或缩放，请求：{RequestVersion}", version);
                    return;
                }
            }

            TextSelectionSnapshot? snapshot;
            await _selectionReadGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                snapshot = await Task.Run(
                        () => _selectionService.TryGetSelectionAsync(
                            manual && ConfigManger.Config.canReadClipboard, token), token)
                    .ConfigureAwait(false);
            }
            finally
            {
                _selectionReadGate.Release();
            }
            token.ThrowIfCancellationRequested();
            if (GetForegroundWindow() != foreground)
            {
                Logger.Debug("划词翻译跳过：读取选区期间前台窗口变化，手动：{Manual}，请求：{RequestVersion}", manual, version);
                return;
            }
            if (!manual && HasWindowMovedOrResized(press, foreground))
            {
                Logger.Debug("自动划词翻译跳过：读取选区期间前台窗口发生移动或缩放，请求：{RequestVersion}", version);
                return;
            }

            sourceWindowBounds = TryGetWindowBounds(foreground, out var boundsRect)
                ? boundsRect
                : null;
            pressPosition = !manual && press is { } p && p.Window == foreground
                ? p.Position
                : null;
            selectionBounds = snapshot?.Bounds;

            if (snapshot is null || snapshot.SourceWindow != foreground || string.IsNullOrWhiteSpace(snapshot.Text))
            {
                Logger.Debug("划词翻译跳过：未读取到当前窗口的有效选区，手动：{Manual}，请求：{RequestVersion}，选区窗口：{SelectionWindow}，前台窗口：{ForegroundWindow}",
                    manual, version, snapshot is null ? "null" : FormatWindow(snapshot.SourceWindow), FormatWindow(foreground));
                if (manual)
                    ShowError(version, Lang.Get("lang.kitopia.selection_translation_no_selection"),
                        releasePosition, sourceWindowBounds, null, pressPosition);
                return;
            }
            if (!manual && (!ConfigManger.Config.selectionTranslationAutoHotKey.IsEnabled ||
                !ConfigManger.Config.selectionTranslationAutoHotKey.CanExecuteInProcess(snapshot.ProcessName)))
            {
                Logger.Debug("自动划词翻译跳过：快捷键已停用或应用被排除，进程：{ProcessName}，请求：{RequestVersion}",
                    snapshot.ProcessName, version);
                return;
            }
            if (snapshot.Text.Length > MaximumTextLength)
            {
                if (manual)
                {
                    ShowError(version, Lang.Get("lang.kitopia.selection_translation_text_too_long"),
                        releasePosition, sourceWindowBounds, selectionBounds, pressPosition);
                }
                else
                {
                    Logger.Debug("自动划词翻译跳过：文本长度超出限制（{TextLength}），请求：{RequestVersion}",
                        snapshot.Text.Length, version);
                }
                return;
            }
            var windowState = await Dispatcher.UIThread.InvokeAsync(() =>
                (IsVisible: _window.IsVisible, IsPinned: _window.ViewModel.IsPinned,
                    IsLoading: _window.ViewModel.IsLoading,
                    SourceLanguage: _window.ViewModel.SourceLanguage,
                    TargetLanguage: _window.ViewModel.TargetLanguage));
            token.ThrowIfCancellationRequested();
            if (!manual && windowState.IsVisible && windowState.IsPinned)
            {
                Logger.Debug("自动划词翻译跳过：窗口已固定，请求：{RequestVersion}", version);
                return;
            }
            if (!manual && windowState.IsVisible && !windowState.IsLoading && IsSameSelection(snapshot))
            {
                Logger.Debug("自动划词翻译跳过：相同选区已显示，请求：{RequestVersion}", version);
                return;
            }

            var sourceLanguage = ConfigManger.Config.selectionTranslationSourceLanguage;
            var targetLanguage = ConfigManger.Config.selectionTranslationTargetLanguage;
            if (windowState.IsVisible)
            {
                sourceLanguage = windowState.SourceLanguage;
                targetLanguage = windowState.TargetLanguage;
            }

            if (!manual)
            {
                var decision = SelectionTranslationTriggerFilter.Evaluate(
                    snapshot.Text,
                    MaximumTextLength,
                    sourceLanguage,
                    targetLanguage,
                    ConfigManger.Config.language,
                    Lang.Current.Culture);
                if (!decision.ShouldTrigger)
                {
                    Logger.Debug("自动划词翻译跳过：{Reason}，文本长度：{TextLength}，请求：{RequestVersion}",
                        decision.Reason, snapshot.Text.Length, version);
                    return;
                }
            }

            Interlocked.CompareExchange(ref _pendingAutomaticCancellation, null, cancellation);
            token.ThrowIfCancellationRequested();

            Dispatcher.UIThread.Post(() =>
            {
                if (version != Volatile.Read(ref _requestVersion)) return;
                var (anchor, screenAnchor) = ResolveWindowAnchors(
                    releasePosition, selectionBounds, sourceWindowBounds, pressPosition);
                _currentSelection = snapshot;
                _window.ViewModel.CanExcludeCurrentProcess =
                    !string.IsNullOrWhiteSpace(snapshot.ProcessName);
                _window.ViewModel.BeginTranslation(snapshot.Text, sourceLanguage, targetLanguage);
                _window.ShowAt(anchor, screenAnchor);
                Logger.Debug("划词翻译显示窗口，进程：{ProcessName}，文本长度：{TextLength}，手动：{Manual}，请求：{RequestVersion}，位置：{Position}，尺寸：{Bounds}",
                    snapshot.ProcessName, snapshot.Text.Length, manual, version, _window.Position, _window.Bounds);
            });

            var translated = await _translationService.TranslateSelectionAsync(
                    snapshot.Text, sourceLanguage, targetLanguage, token)
                .ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (version != Volatile.Read(ref _requestVersion) || !_window.IsVisible) return;
                _window.ViewModel.SetTranslatedText(translated);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Logger.Debug("划词翻译请求已取消，手动：{Manual}，请求：{RequestVersion}", manual, version);
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "划词翻译失败");
            ShowError(version, Lang.Get("lang.kitopia.selection_translation_failed"),
                releasePosition, sourceWindowBounds, selectionBounds, pressPosition);
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingAutomaticCancellation, null, cancellation);
        }
    }

    private void TranslateCurrentSelection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(TranslateCurrentSelection);
            return;
        }

        if (_currentSelection is null || !_window.IsVisible) return;
        _ = TranslateExistingSelectionAsync(_currentSelection);
    }

    private void RetryCurrentSelection()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RetryCurrentSelection);
            return;
        }

        if (_currentSelection is null) return;
        _ = TranslateExistingSelectionAsync(_currentSelection);
    }

    private void ExcludeCurrentProcess()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ExcludeCurrentProcess);
            return;
        }

        var processName = _currentSelection?.ProcessName;
        if (string.IsNullOrWhiteSpace(processName)) return;

        var normalized = HotKeyModel.NormalizeProcessName(processName);
        var candidate = new HotKeyModel(ConfigManger.Config.selectionTranslationAutoHotKey);
        if (candidate.ProcessScope == HotKeyProcessScope.Include)
        {
            candidate.ProcessNames = candidate.ProcessNames.Where(name =>
                !string.Equals(HotKeyModel.NormalizeProcessName(name), normalized,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        else
        {
            candidate.ProcessNames = candidate.ProcessScope == HotKeyProcessScope.All
                ? [normalized]
                : candidate.ProcessNames.Append(normalized).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            candidate.ProcessScope = HotKeyProcessScope.Exclude;
        }
        if (_hotkeys.Modify(candidate)) CloseWindow();
    }

    private async Task TranslateExistingSelectionAsync(TextSelectionSnapshot snapshot)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => _ = TranslateExistingSelectionAsync(snapshot));
            return;
        }

        var sourceLanguage = _window.ViewModel.SourceLanguage;
        var targetLanguage = _window.ViewModel.TargetLanguage;
        var (version, _, token) = BeginRequest();
        Dispatcher.UIThread.Post(() =>
        {
            if (version == Volatile.Read(ref _requestVersion))
                _window.ViewModel.BeginTranslation(snapshot.Text, sourceLanguage, targetLanguage);
        });
        try
        {
            var translated = await _translationService.TranslateSelectionAsync(
                    snapshot.Text, sourceLanguage, targetLanguage, token)
                .ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                if (version == Volatile.Read(ref _requestVersion) && _window.IsVisible)
                    _window.ViewModel.SetTranslatedText(translated);
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Logger.Warning(exception, "重新翻译选区失败");
            ShowError(version, Lang.Get("lang.kitopia.selection_translation_failed"));
        }
    }

    private (int Version, CancellationTokenSource Cancellation, CancellationToken Token) BeginRequest(
        bool trackPendingAutomatic = false)
    {
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        Interlocked.Exchange(ref _pendingAutomaticCancellation, trackPendingAutomatic ? cancellation : null);
        var previous = Interlocked.Exchange(ref _requestCancellation, cancellation);
        previous?.Cancel();
        previous?.Dispose();
        return (Interlocked.Increment(ref _requestVersion), cancellation, token);
    }

    private void CancelCurrentRequest()
    {
        Interlocked.Exchange(ref _pendingAutomaticCancellation, null);
        var cancellation = Interlocked.Exchange(ref _requestCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
        Interlocked.Increment(ref _requestVersion);
    }

    private void CancelPendingAutomaticTrigger()
    {
        var pending = Interlocked.Exchange(ref _pendingAutomaticCancellation, null);
        if (pending is null) return;
        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsSameSelection(TextSelectionSnapshot snapshot) =>
        _currentSelection is { } previous &&
        previous.SourceWindow == snapshot.SourceWindow &&
        string.Equals(previous.Text, snapshot.Text, StringComparison.Ordinal);

    private bool IsInsideWindow(int x, int y)
    {
        var scaling = _window.RenderScaling > 0
            ? _window.RenderScaling
            : (_window.Screens.ScreenFromWindow(_window)?.Scaling ?? 1);
        var bounds = _window.Bounds;
        var left = _window.Position.X;
        var top = _window.Position.Y;
        var right = left + (int)Math.Ceiling(bounds.Width * scaling);
        var bottom = top + (int)Math.Ceiling(bounds.Height * scaling);
        return x >= left && x <= right && y >= top && y <= bottom;
    }

    private (PixelPoint? Anchor, PixelPoint? ScreenAnchor) ResolveWindowAnchors(
        PixelPoint? releasePosition,
        TextSelectionBounds? selectionBounds,
        PixelRect? sourceWindowBounds,
        PixelPoint? pressPosition)
    {
        var screens = _window.Screens.All;
        if (screens.Count == 0)
            return (releasePosition, releasePosition);

        var screenRects = new PixelRect[screens.Count];
        for (var index = 0; index < screens.Count; index++)
            screenRects[index] = screens[index].Bounds;

        var resolved = SelectionTranslationWindow.ResolvePlacementTarget(
            screenRects,
            releasePosition,
            selectionBounds,
            sourceWindowBounds,
            pressPosition);
        if (resolved is not { } target)
            return (releasePosition, releasePosition);

        var screenAnchor = SelectionTranslationWindow.ClampPointInside(
            target.Anchor,
            screenRects[target.ScreenIndex]);
        return (target.Anchor, screenAnchor);
    }

    private static bool HasWindowMovedOrResized(MousePressState? press, nint foreground)
    {
        if (press is null || press.Window != foreground || press.WindowBounds is not { } previousBounds)
            return false;

        return TryGetWindowBounds(foreground, out var currentBounds) && currentBounds != previousBounds;
    }

    private static bool TryGetWindowBounds(nint hwnd, out PixelRect bounds)
    {
        bounds = default;
        if (hwnd == 0) return false;

        var handle = new HWND(hwnd);
        if (ScreenCaptureInfoEx.TryGetExtendedFrameBounds(handle, out var extended) &&
            extended.Width > 0 && extended.Height > 0)
        {
            bounds = new PixelRect(extended.Left, extended.Top, extended.Width, extended.Height);
            return true;
        }

        if (User32.GetWindowRect(handle, out var rect) && rect.Width > 0 && rect.Height > 0)
        {
            bounds = new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height);
            return true;
        }

        return false;
    }

    private void ShowError(
        int version,
        string message,
        PixelPoint? releasePosition = null,
        PixelRect? sourceWindowBounds = null,
        TextSelectionBounds? selectionBounds = null,
        PixelPoint? pressPosition = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (version != Volatile.Read(ref _requestVersion)) return;
            _window.ViewModel.SetError(message);
            if (!_window.IsVisible)
            {
                var (anchor, screenAnchor) = ResolveWindowAnchors(
                    releasePosition, selectionBounds, sourceWindowBounds, pressPosition);
                _window.ShowAt(anchor, screenAnchor);
            }
        });
    }

    private void CloseWindow()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(CloseWindow);
            return;
        }

        _pressedInsideWindow = false;
        CancelCurrentRequest();
        _window.ViewModel.CanExcludeCurrentProcess = false;
        _window.Hide();
    }

    private sealed record MousePressState(nint Window, PixelRect? WindowBounds, PixelPoint Position);

    internal static string FormatWindow(nint hwnd)
    {
        if (hwnd == 0) return "null";
        var title = GetWindowTitle(hwnd);
        return string.IsNullOrWhiteSpace(title)
            ? hwnd.ToString()
            : $"{title} ({hwnd})";
    }

    internal static string GetWindowTitle(nint hwnd)
    {
        if (hwnd == 0) return string.Empty;
        try
        {
            var length = User32.GetWindowTextLength((HWND)hwnd);
            if (length <= 0) return string.Empty;
            var sb = new StringBuilder(length + 1);
            User32.GetWindowText((HWND)hwnd, sb, sb.Capacity);
            return sb.ToString().Replace("\r", "").Replace("\n", " ").Trim();
        }
        catch
        {
            return string.Empty;
        }
    }
}
#endif
